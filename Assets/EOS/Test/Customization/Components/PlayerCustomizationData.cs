using System;
using EOS.Core;
using UnityEngine;

namespace EOS.Test.Customization
{
    [Serializable]
    public class PlayerCustomizationData : IMergeableCloudData<PlayerCustomizationData>
    {
        public const string CloudFileName = "customization.json";

        public Color color = Color.white;
        public bool hasPet = true;
        public Color petColor = Color.white;

        public bool IsSimilarTo(PlayerCustomizationData other)
        {
            return other != null &&
                   color == other.color &&
                   hasPet == other.hasPet &&
                   petColor == other.petColor;
        }

        public void MergeFromServer(PlayerCustomizationData serverData)
        {
            if (serverData == null)
            {
                return;
            }

            color = serverData.color;
            hasPet = serverData.hasPet;
            petColor = serverData.petColor;
        }

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
