using System;
using UnityEngine;

namespace EOS.Test.Customization
{
    [Serializable]
    public class PlayerCustomizationData
    {
        public const string CloudFileName = "customization.json";

        public Color color = Color.white;
        public bool hasPet = true;
        public Color petColor = Color.white;

        public static Vector3 ColorToVector3(Color c)
        {
            return new Vector3(c.r, c.g, c.b);
        }

        public static Color Vector3ToColor(Vector3 v)
        {
            return new Color(v.x, v.y, v.z, 1f);
        }
    }
}