using UnityEngine;

namespace OldChronianDinghy
{
    internal static class Geo
    {
        // Mesh bounds of root (incl. children) in the coordinates of `space`, inside a frame rotated by `frame`
        public static Bounds Measure(Transform root, Transform space, Quaternion frame)
        {
            Quaternion inv = Quaternion.Inverse(frame);
            bool first = true;
            var bounds = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = inv * space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }
    }
}