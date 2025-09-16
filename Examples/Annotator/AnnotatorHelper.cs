using CycleGUI.API;
using CycleGUI;
using Python.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Annotator
{
    public class AnnotatorHelper
    {
        public static float CalculateAltitude(Vector3 cameraPos, Vector3 lookAt)
        {
            float dx = cameraPos.X - lookAt.X;
            float dy = cameraPos.Y - lookAt.Y;
            float dz = cameraPos.Z - lookAt.Z;

            float horizontalDistance = MathF.Sqrt(dx * dx + dy * dy);
            float altitude = MathF.Atan2(dz, horizontalDistance); // Angle in radians
            return altitude;
        }

        public static float CalculateAzimuth(Vector3 cameraPos, Vector3 lookAt)
        {
            return MathF.Atan2(cameraPos.Y - lookAt.Y, cameraPos.X - lookAt.X);
            //return MathHelper.ToDegrees(MathF.Atan2(cameraPos.Y - lookAt.Y, cameraPos.X - lookAt.X));
        }

        public static float CalculateDistance(Vector3 cameraPos, Vector3 lookAt)
        {
            return Vector3.Distance(cameraPos, lookAt);
        }
    }
}
