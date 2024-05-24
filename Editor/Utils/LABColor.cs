using System;
using UnityEngine;

namespace Lattice.Editor.Utils
{
    /// <summary>
    ///     This provides a Lab color space in addition to Unity's built in Red/Green/Blue colors. Lab is based on CIE XYZ
    ///     and is a color-opponent space with L for lightness and a and b for the color-opponent dimensions. Lab color is
    ///     designed to approximate human vision and so it aspires to perceptual uniformity. The L component closely matches
    ///     human perception of lightness.
    /// </summary>
    [Serializable]
    public struct LabColor
    {
        /// <summary>Lightness in the range 0 to 100.</summary>
        public float L;

        /// <summary>The "A" value of the chroma.</summary>
        public float A;

        /// <summary>The "B" value of the chroma.</summary>
        public float B;

        /// <summary>Creates a new color in the lab color space.</summary>
        public LabColor(float lightness, float aChroma, float bChroma)
        {
            L = lightness;
            A = aChroma;
            B = bChroma;
        }

        /// <summary>Creates a new color in the LAB color space from a Unity color.</summary>
        public LabColor(Color col)
        {
            LabColor temp = FromColor(col);
            L = temp.L;
            A = temp.A;
            B = temp.B;
        }

        /// <summary>Lerps two LABColors.</summary>
        public static LabColor Lerp(LabColor a, LabColor b, float t)
        {
            return new LabColor(Mathf.Lerp(a.L, b.L, t), Mathf.Lerp(a.A, b.A, t), Mathf.Lerp(a.B, b.B, t));
        }

        /// <summary> Static function for returning the color difference in a normalized colorspace (Delta-E)</summary>
        /// .
        public static float Distance(LabColor a, LabColor b)
        {
            return Mathf.Sqrt(Mathf.Pow(a.L - b.L, 2f) + Mathf.Pow(a.A - b.A, 2f) + Mathf.Pow(a.B - b.B, 2f));
        }

        /// <summary>Converts a Unity color to a LABColor.</summary>
        public static LabColor FromColor(Color c)
        {
            float D65x = 0.9505f;
            float D65y = 1.0f;
            float D65z = 1.0890f;
            float rLinear = c.r;
            float gLinear = c.g;
            float bLinear = c.b;
            float r = rLinear > 0.04045f ? Mathf.Pow((rLinear + 0.055f) / (1f + 0.055f), 2.2f) : rLinear / 12.92f;
            float g = gLinear > 0.04045f ? Mathf.Pow((gLinear + 0.055f) / (1f + 0.055f), 2.2f) : gLinear / 12.92f;
            float b = bLinear > 0.04045f ? Mathf.Pow((bLinear + 0.055f) / (1f + 0.055f), 2.2f) : bLinear / 12.92f;
            float x = r * 0.4124f + g * 0.3576f + b * 0.1805f;
            float y = r * 0.2126f + g * 0.7152f + b * 0.0722f;
            float z = r * 0.0193f + g * 0.1192f + b * 0.9505f;
            x = x > 0.9505f ? 0.9505f : x < 0f ? 0f : x;
            y = y > 1.0f ? 1.0f : y < 0f ? 0f : y;
            z = z > 1.089f ? 1.089f : z < 0f ? 0f : z;
            LabColor lab = new LabColor(0f, 0f, 0f);
            float fx = x / D65x;
            float fy = y / D65y;
            float fz = z / D65z;
            fx = fx > 0.008856f ? Mathf.Pow(fx, 1.0f / 3.0f) : 7.787f * fx + 16.0f / 116.0f;
            fy = fy > 0.008856f ? Mathf.Pow(fy, 1.0f / 3.0f) : 7.787f * fy + 16.0f / 116.0f;
            fz = fz > 0.008856f ? Mathf.Pow(fz, 1.0f / 3.0f) : 7.787f * fz + 16.0f / 116.0f;
            lab.L = 116.0f * fy - 16f;
            lab.A = 500.0f * (fx - fy);
            lab.B = 200.0f * (fy - fz);
            return lab;
        }

        /// <summary>Converts a LABColor to a normal unity Color.</summary>
        public static Color ToColor(LabColor lab)
        {
            float D65x = 0.9505f;
            float D65y = 1.0f;
            float D65z = 1.0890f;
            float delta = 6.0f / 29.0f;
            float fy = (lab.L + 16f) / 116.0f;
            float fx = fy + lab.A / 500.0f;
            float fz = fy - lab.B / 200.0f;
            float x = fx > delta ? D65x * (fx * fx * fx) : (fx - 16.0f / 116.0f) * 3f * (delta * delta) * D65x;
            float y = fy > delta ? D65y * (fy * fy * fy) : (fy - 16.0f / 116.0f) * 3f * (delta * delta) * D65y;
            float z = fz > delta ? D65z * (fz * fz * fz) : (fz - 16.0f / 116.0f) * 3f * (delta * delta) * D65z;
            float r = x * 3.2410f - y * 1.5374f - z * 0.4986f;
            float g = -x * 0.9692f + y * 1.8760f - z * 0.0416f;
            float b = x * 0.0556f - y * 0.2040f + z * 1.0570f;
            r = r <= 0.0031308f ? 12.92f * r : (1f + 0.055f) * Mathf.Pow(r, 1.0f / 2.4f) - 0.055f;
            g = g <= 0.0031308f ? 12.92f * g : (1f + 0.055f) * Mathf.Pow(g, 1.0f / 2.4f) - 0.055f;
            b = b <= 0.0031308f ? 12.92f * b : (1f + 0.055f) * Mathf.Pow(b, 1.0f / 2.4f) - 0.055f;
            r = r < 0 ? 0 : r;
            g = g < 0 ? 0 : g;
            b = b < 0 ? 0 : b;
            return new Color(r, g, b);
        }

        /// <summary>Converts this to a normal unity Color.</summary>
        public Color ToColor()
        {
            return ToColor(this);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "L:" + L + " A:" + A + " B:" + B;
        }
    }
}
