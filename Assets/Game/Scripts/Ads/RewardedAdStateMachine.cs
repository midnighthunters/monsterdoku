using System;
using MonsterLogic.Services;

namespace MonsterLogic.Ads
{
    public sealed class RewardedAdStateMachine
    {
        private Action<RewardedAdResult> _completed;
        private bool _earned;

        public bool HasPendingRequest => _completed != null;

        public bool TryBegin(Action<RewardedAdResult> completed)
        {
            if (completed == null || HasPendingRequest) return false;
            _completed = completed;
            _earned = false;
            return true;
        }

        public void MarkRewardEarned()
        {
            if (HasPendingRequest) _earned = true;
        }

        public void CompleteHidden() => Complete(_earned ? RewardedAdResult.Earned : RewardedAdResult.DismissedWithoutReward);
        public void CompleteDisplayFailed() => Complete(RewardedAdResult.DisplayFailed);

        public void CancelWithoutCallback()
        {
            _completed = null;
            _earned = false;
        }

        private void Complete(RewardedAdResult result)
        {
            var callback = _completed;
            if (callback == null) return;
            _completed = null;
            _earned = false;
            callback(result);
        }
    }
}
