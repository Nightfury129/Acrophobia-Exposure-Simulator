using UnityEngine;

namespace VRSlashGame.Events
{
    /// <summary>
    /// Encapsulates slice event details when a fruit or sliceable object is cut.
    /// </summary>
    public struct FruitSliceData
    {
        public Vector3 SlicePosition { get; }
        public Vector3 SliceDirection { get; }
        public int BasePoints { get; }
        public int ComboCount { get; }
        public string FruitType { get; }

        public FruitSliceData(Vector3 slicePosition, Vector3 sliceDirection, int basePoints, int comboCount, string fruitType = "Standard")
        {
            SlicePosition = slicePosition;
            SliceDirection = sliceDirection;
            BasePoints = basePoints;
            ComboCount = comboCount;
            FruitType = fruitType;
        }
    }
}
