using UnityEngine;

namespace RidgeRelay
{
    public static class CourierAppearance
    {
        public static readonly string[] Names = { "CORAL", "SAND", "CREAM", "SLATE", "LAVENDER", "MINT" };
        public static readonly Color[] FurColors =
        {
            new(0.79f, 0.28f, 0.17f), new(0.88f, 0.65f, 0.40f), new(0.95f, 0.84f, 0.65f),
            new(0.42f, 0.49f, 0.59f), new(0.65f, 0.46f, 0.72f), new(0.41f, 0.64f, 0.50f)
        };
        private static int selected = -1;

        public static int Selected
        {
            get
            {
                if (selected < 0) selected = Mathf.Clamp(PlayerPrefs.GetInt("RidgeRelay.FurColor", 1), 0, Names.Length - 1);
                return selected;
            }
            set
            {
                selected = Mathf.Clamp(value, 0, Names.Length - 1);
                PlayerPrefs.SetInt("RidgeRelay.FurColor", selected);
                PlayerPrefs.Save();
            }
        }
    }
}
