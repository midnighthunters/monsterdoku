using UnityEngine;

namespace MonsterLogic.UI
{
    /// <summary>
    /// Explicit visual identities for the runtime-built gameplay screen.
    /// References are serialized so panel.png sub-asset order can never select artwork.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSceneSpriteSet", menuName = "Monster Logic/Game Scene Sprite Set")]
    public sealed class GameSceneSpriteSet : ScriptableObject
    {
        [Header("Navigation and header")]
        public Sprite BackButton;
        public Sprite SettingsButton;
        public Sprite HeaderPlaque;

        [Header("Statistics")]
        public Sprite StatsPanel;
        public Sprite MonsterIcon;
        public Sprite HourglassIcon;
        public Sprite HeartIcon;

        [Header("Rule cards")]
        public Sprite RuleRegion;
        public Sprite RuleRowColumn;
        public Sprite RuleNoTouch;

        [Header("Gameplay help and boosters")]
        public Sprite InstructionStrip;
        public Sprite XButton;
        public Sprite BoosterCircle;
        public Sprite HintButton;
        public Sprite BoosterCountPill;
        public Sprite MummyBoosterAvatar;
        public Sprite PlayAdIcon;

        [Header("Environment and cell treatment")]
        public Sprite CellBatWatermark;
        public Sprite TombstoneDecoration;
        public Sprite Pumpkin;
        public Sprite Candle;
        public Sprite BottomBanner;
        public Sprite PurpleBar;
        public Sprite YellowStar;
    }
}