using System.Linq;
using UnityEngine;

namespace GlorianaDinghy
{
    internal static class ShipTweaks
    {
        private static readonly float DeckSlope = Mathf.Tan(5.8f * Mathf.Deg2Rad);

        public static void Apply(Transform g, Transform walk)
        {
            MoveCapstan(g, walk);
            MoveSheetBlock(g);
        }

        private static void MoveCapstan(Transform g, Transform walk)
        {
            var cap = g.Find("0_CAPSTAN");
            if (cap == null) { Plugin.Log.LogWarning("0_CAPSTAN not found"); return; }
            var anchorCtl = g.Find("anchor controller");
            var walkCap = walk == null ? null
                : walk.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "0_CAPSTAN");

            Vector3 pos0 = cap.localPosition;
            float minY0 = Geo.Measure(cap, g, Quaternion.identity).min.y;
            float dx = S.CapstanShift;

            cap.localScale *= S.CapstanScale;
            cap.localPosition = pos0 + new Vector3(dx, dx * DeckSlope, 0f);
            // scaling happens around the pivot, put the base back on the deck
            cap.localPosition += Vector3.up * (minY0 + dx * DeckSlope - Geo.Measure(cap, g, Quaternion.identity).min.y);

            Vector3 delta = cap.localPosition - pos0;
            if (anchorCtl != null) anchorCtl.localPosition += delta;
            if (walkCap != null)
            {
                walkCap.position = walk.TransformPoint(walk.InverseTransformPoint(walkCap.position) + delta);
                walkCap.localScale *= S.CapstanScale;
            }
        }

        private static void MoveSheetBlock(Transform g)
        {
            var block = g.Find("structure_container/ROPES/" + S.BlockName);
            if (block == null) { Plugin.Log.LogWarning("Sheet block not found: " + S.BlockName); return; }
            var mast = g.Find("structure_container/MAST_MAIN");
            var cc = mast != null ? mast.GetComponent<CapsuleCollider>() : null;

            Vector3 p = g.InverseTransformPoint(block.position) + Vector3.up * S.BlockLift;
            Quaternion rot0 = Quaternion.Inverse(g.rotation) * block.rotation;
            Quaternion rot = rot0;

            if (cc != null && block.GetComponentsInChildren<MeshFilter>().Length > 0)
            {
                Vector3 ax = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 a = g.InverseTransformPoint(mast.TransformPoint(cc.center));
                Vector3 dir = g.InverseTransformDirection(mast.TransformDirection(ax)).normalized;
                float r = cc.radius * Mathf.Abs(mast.lossyScale.x);

                Vector3 toMast = a + dir * Vector3.Dot(p - a, dir) - p;
                toMast.y = 0f;
                float dist = toMast.magnitude;
                if (dist > 0.01f)
                {
                    toMast /= dist;
                    Vector3 tangent = Vector3.Cross(Vector3.up, toMast).normalized;
                    rot = Quaternion.AngleAxis(S.BlockSpin, toMast)
                        * Quaternion.AngleAxis(S.BlockTilt, tangent)
                        * Quaternion.FromToRotation(Vector3.down, toMast) * rot0;

                    block.rotation = g.rotation * rot;
                    block.position = g.TransformPoint(p);
                    Quaternion frame = Quaternion.LookRotation(toMast, Vector3.up);
                    Bounds bb = Geo.Measure(block, g, frame);
                    float reach = bb.max.z - (Quaternion.Inverse(frame) * p).z;
                    p += toMast * ((dist - reach) - (r + S.BlockGap));
                }
            }

            block.rotation = g.rotation * rot;
            block.position = g.TransformPoint(p);
        }
    }
}