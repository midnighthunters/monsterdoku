#!/usr/bin/env python3
"""
Prune orphaned CI-generated development certificates and invalid provisioning profiles
from Apple Developer via the App Store Connect API.

Ephemeral CI runners (GitHub Actions, CircleCI) request new development certificates
with Xcode's -allowProvisioningUpdates. Because their private keys are destroyed when
runners terminate, these certificates accumulate until Apple's per-account development
certificate limit is reached, causing:
  "error: Choose a certificate to revoke. Your account has reached the maximum number of certificates."

This script safely revokes ONLY orphaned "Created via API" development certificates,
freeing up the quota for the active build. Personal certificates and distribution
certificates are strictly preserved.
"""

import os
import sys
import time

try:
    import jwt
    import requests
except ImportError:
    print("[prune-ci-certs] Warning: pyjwt and/or requests not available. Skipping cleanup.")
    sys.exit(0)

key_id = os.environ.get("APP_STORE_CONNECT_KEY_ID", "").strip()
issuer_id = os.environ.get("APP_STORE_CONNECT_ISSUER_ID", "").strip()
private_key = os.environ.get("APP_STORE_CONNECT_PRIVATE_KEY", "").strip()
bundle_id = os.environ.get("IOS_BUNDLE_ID", "com.zemolabs.monsterdoku").strip()

if not key_id or not issuer_id or not private_key:
    print("[prune-ci-certs] App Store Connect API credentials not found in environment. Skipping.")
    sys.exit(0)

# Normalize private key if escaped newlines are present
if "\\n" in private_key and "-----BEGIN" in private_key:
    private_key = private_key.replace("\\n", "\n")

header = {"alg": "ES256", "kid": key_id, "typ": "JWT"}
payload = {
    "iss": issuer_id,
    "iat": int(time.time()),
    "exp": int(time.time()) + 300,
    "aud": "appstoreconnect-v1",
}

try:
    token = jwt.encode(payload, private_key, algorithm="ES256", headers=header)
except Exception as err:
    print(f"[prune-ci-certs] Error generating App Store Connect JWT: {err}")
    sys.exit(1)

headers = {
    "Authorization": f"Bearer {token}",
    "Content-Type": "application/json",
}
base_url = "https://api.appstoreconnect.apple.com/v1"


def get_all(endpoint_path: str):
    items = []
    url = f"{base_url}/{endpoint_path}"
    while url:
        resp = requests.get(url, headers=headers, timeout=30)
        if resp.status_code != 200:
            print(f"[prune-ci-certs] GET {url} failed (HTTP {resp.status_code}): {resp.text}")
            break
        body = resp.json()
        items.extend(body.get("data", []))
        url = body.get("links", {}).get("next")
    return items


def main():
    print("=== App Store Connect CI Certificate & Profile Hygiene ===")

    # 1. Fetch all development certificates
    print("[prune-ci-certs] Fetching development certificates...")
    certs = get_all("certificates?filter[certificateType]=DEVELOPMENT,IOS_DEVELOPMENT&limit=100")
    print(f"[prune-ci-certs] Found {len(certs)} active development certificate(s).")

    revoked_certs = 0
    for cert in certs:
        attrs = cert.get("attributes", {})
        cert_id = cert.get("id")
        name = attrs.get("name", "")
        display_name = attrs.get("displayName", "")
        cert_type = attrs.get("certificateType", "")

        # Target ONLY certificates generated via API on CI runners
        # Strictly preserve personal developer certificates (e.g. "iOS Development: Nikhil Goyal")
        # and all distribution certificates.
        is_ci_created = (
            display_name == "Created via API"
            or name.startswith("iOS Development: Created via API")
            or name.startswith("Apple Development: Created via API")
        )
        is_dev = cert_type in ("DEVELOPMENT", "IOS_DEVELOPMENT")

        if is_ci_created and is_dev:
            print(f"[prune-ci-certs] Revoking orphaned CI certificate {cert_id} ('{name}', type={cert_type})...")
            del_resp = requests.delete(f"{base_url}/certificates/{cert_id}", headers=headers, timeout=30)
            if del_resp.status_code in (200, 204):
                print(f"  -> Successfully revoked certificate {cert_id}")
                revoked_certs += 1
            else:
                print(f"  -> Failed to revoke certificate {cert_id} (HTTP {del_resp.status_code}): {del_resp.text}")
        else:
            print(f"[prune-ci-certs] Preserving developer certificate: {cert_id} ('{name}', type={cert_type})")

    print(f"[prune-ci-certs] Pruned {revoked_certs} orphaned development certificate(s).")

    # 2. Fetch and remove INVALID development provisioning profiles
    print(f"[prune-ci-certs] Checking for invalid development provisioning profiles for {bundle_id}...")
    profiles = get_all("profiles?filter[profileType]=IOS_APP_DEVELOPMENT&limit=100")
    deleted_profiles = 0
    for profile in profiles:
        p_id = profile.get("id")
        p_attrs = profile.get("attributes", {})
        p_name = p_attrs.get("name", "")
        p_state = p_attrs.get("profileState", "")

        if p_state == "INVALID" and (bundle_id in p_name or "iOS Team Provisioning Profile" in p_name):
            print(f"[prune-ci-certs] Deleting invalid development profile {p_id} ('{p_name}')...")
            del_resp = requests.delete(f"{base_url}/profiles/{p_id}", headers=headers, timeout=30)
            if del_resp.status_code in (200, 204):
                print(f"  -> Successfully deleted profile {p_id}")
                deleted_profiles += 1
            else:
                print(f"  -> Failed to delete profile {p_id} (HTTP {del_resp.status_code}): {del_resp.text}")

    print(f"[prune-ci-certs] Pruned {deleted_profiles} invalid development profile(s).")
    print("=== Certificate cleanup complete ===")


if __name__ == "__main__":
    main()
