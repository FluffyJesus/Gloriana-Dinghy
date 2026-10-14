using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace GlorianaDinghy
{
    internal static class DeckStow
    {
        private const string DavitsName = "davits_0_gloriana";
        private const string MarkerName = "glorianadinghy_applied";

        private static System.Type davitsType, dinghyType;
        private static MethodInfo stowMethod, launchMethod;
        private static Component davits;
        private static Transform boat, g, walk;
        private static float nextTry;

        private static float sx1 = 2.15f, sy1 = 1.30f, sx2 = 5.15f, sy2 = 1.40f;
        private static readonly GameObject[] spars = new GameObject[2];
        private static readonly LineRenderer[] lashings = new LineRenderer[2];
        private static readonly List<GameObject> chocks = new List<GameObject>();
        private static Material woodMat;

        private static HoistCoil coil;
        private static string coilText;

        private static GameObject stowed;
        private static Transform stowedParent;
        private static Vector3 origScale = Vector3.one;
        private static Rigidbody stowedRb;
        private static RigidbodyInterpolation oldInterp;
        private static readonly List<Behaviour> disabledComps = new List<Behaviour>();
        private static readonly List<Behaviour> clipInputs = new List<Behaviour>();
        private static Collider[] stowedCols, shipCols;
        private static BoxCollider walkBox;
        private static float boatLength = 8f;
        private static GameObject launching;
        private static float restOffset = float.NaN;

        private static bool notifyLooked;
        private static MethodInfo notifyMethod;
        private static FieldInfo notifyInstance;

        public static bool IsStowed => stowed != null;

        public static void Tick()
        {
            if (davits == null) { TryInit(); return; }
            var key = Plugin.HoistKey.Value;
            if (key != KeyCode.None && Input.GetKeyDown(key)) Toggle();
            UpdateCoilText();
        }

        public static void Toggle()
        {
            if (davits == null) return;
            if (IsStowed) Launch();
            else TryHoist();
        }

        // ---------- setup ----------
        private static void TryInit()
        {
            if (Time.time < nextTry) return;
            nextTry = Time.time + 2f;

            if (davitsType == null) davitsType = AccessTools.TypeByName("DinghiesScripts.Davits");
            if (dinghyType == null) dinghyType = AccessTools.TypeByName("DinghiesScripts.Dinghy");
            if (davitsType == null || dinghyType == null) return;

            var refs = Object.FindObjectsOfType<BoatRefs>().FirstOrDefault(r => r.name.StartsWith("BOAT GLORIANA"));
            if (refs == null) return;
            boat = refs.transform;
            g = refs.boatModel;
            walk = refs.walkCol;
            ResetState();

            if (g.Find(MarkerName) == null)   // never move the capstan twice
            {
                ShipTweaks.Apply(g, walk);
                new GameObject(MarkerName).transform.SetParent(g, false);
            }
            MeasureHatches();

            if (stowMethod == null)
            {
                stowMethod = AccessTools.Method(davitsType, "Stow");
                launchMethod = AccessTools.Method(davitsType, "Launch");
                Plugin.H.Patch(stowMethod,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(DeckStow), nameof(StowPostfix))));
                Plugin.H.Patch(launchMethod,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DeckStow), nameof(LaunchPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(DeckStow), nameof(LaunchPostfix))));
            }

            var old = g.Find(DavitsName);
            if (old != null) davits = old.GetComponent(davitsType);
            if (davits == null)
            {
                var go = new GameObject(DavitsName);
                go.SetActive(false);
                go.transform.SetParent(g, false);
                go.transform.localPosition = new Vector3(4f, 2f, 0f);
                davits = go.AddComponent(davitsType);
                var tr = Traverse.Create(davits);
                tr.Field("tarpPackage").SetValue(Child(go, "tarp_package"));
                tr.Field("winch0").SetValue(Child(go, "winch0"));
                tr.Field("winch1").SetValue(Child(go, "winch1"));
                go.SetActive(true);
            }

            if (g.Find("dinghy_hoist_coil") == null) CreateCoil();
            Plugin.Log.LogInfo("Deck stowage ready on " + boat.name);
        }

        private static void ResetState()
        {
            for (int i = 0; i < 2; i++) { spars[i] = null; lashings[i] = null; }
            chocks.Clear();
            woodMat = null; walkBox = null; coil = null; coilText = null;
            stowed = null; stowedRb = null; stowedCols = null; shipCols = null;
            disabledComps.Clear(); clipInputs.Clear();
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var c = new GameObject(name);
            c.transform.SetParent(parent.transform, false);
            return c;
        }

        private static void MeasureHatches()
        {
            var gr = g.GetComponentsInChildren<GPButtonTrapdoor>(true)
                      .Where(HatchLock.IsCargoHatch)
                      .Select(t => Geo.Measure(t.transform, g, Quaternion.identity))
                      .OrderBy(b => b.center.x)
                      .ToList();
            if (gr.Count < 2) return;
            Bounds a = gr[0], b = gr[gr.Count - 1];
            sx1 = a.min.x + a.size.x * 0.25f; sy1 = a.max.y;
            sx2 = b.max.x - b.size.x * 0.25f; sy2 = b.max.y;
        }

        private static float Pitch() => Mathf.Atan2(sy2 - sy1, sx2 - sx1) * Mathf.Rad2Deg;

        // ---------- coil ----------
                private static void CreateCoil()
        {
            var src = g.Find("0_CAPSTAN/coiled_rope");
            var mf = src != null ? src.GetComponent<MeshFilter>() : null;
            var mr = src != null ? src.GetComponent<MeshRenderer>() : null;
            if (mf == null || mr == null) { Plugin.Log.LogWarning("Coil model not found"); return; }

            var go = new GameObject("dinghy_hoist_coil");
            go.transform.SetParent(g, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;

            // lay the coil flat: its thinnest mesh axis -> ship up (the capstan itself is tilted)
            Vector3 ext = mf.sharedMesh.bounds.extents;
            Vector3 thin = ext.x <= ext.y && ext.x <= ext.z ? Vector3.right
                         : ext.y <= ext.z ? Vector3.up : Vector3.forward;
            Quaternion r0 = Quaternion.Inverse(g.rotation) * src.rotation;
            Vector3 axis = r0 * thin;
            if (axis.y < 0f) axis = -axis;
            Quaternion flat = Quaternion.FromToRotation(axis, Vector3.up) * r0;
            go.transform.localRotation = flat;

            Bounds b = Geo.Measure(go.transform, g, Quaternion.identity);
            go.transform.localScale = Vector3.one * (S.CoilDiameter / Mathf.Max(b.size.x, b.size.z, 0.01f));

            var fife = g.Find("structure_container/FIFE_MAIN");
            float x = fife != null ? Geo.Measure(fife, g, Quaternion.identity).min.x - S.CoilBehindFife : -0.75f;
            float z = S.CoilSideZ;
            if (!SurfaceAt(x, z, 2.5f, out float y0, out _)) y0 = 1.0f;

            // local deck plane from 4 samples around the coil (outliers like rails are ignored)
            float r = S.CoilDiameter * 0.45f;
            float H(float px, float pz) =>
                SurfaceAt(px, pz, y0 + 0.3f, out float y, out _) && Mathf.Abs(y - y0) < 0.1f ? y : y0;
            float slopeX = (H(x + r, z) - H(x - r, z)) / (2f * r);
            float slopeZ = (H(x, z + r) - H(x, z - r)) / (2f * r);
            Vector3 n = new Vector3(-slopeX, 1f, -slopeZ).normalized;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, n);
            go.transform.localRotation = tilt * flat;

            // put the bottom of the coil onto that plane, slightly sunk
            b = Geo.Measure(go.transform, g, tilt);
            Vector3 dp = Quaternion.Inverse(tilt) * new Vector3(x, y0, z);
            go.transform.localPosition += tilt * new Vector3(dp.x - b.center.x, dp.y - S.CoilSink - b.min.y, dp.z - b.center.z);
            Plugin.Log.LogInfo($"Coil on deck: y {y0:F3}, slope x {slopeX:F3}, z {slopeZ:F3}");

            go.AddComponent<BoxCollider>();
            var outlineType = AccessTools.TypeByName("cakeslice.Outline");
            Component ol = outlineType != null ? go.AddComponent(outlineType) : null;
            if (ol is Behaviour ob) ob.enabled = false;
            coil = go.AddComponent<HoistCoil>();
            if (ol != null) Traverse.Create(coil).Field("outline").SetValue(ol);
        }

        private static void UpdateCoilText()
        {
            if (coil == null) return;
            string t = IsStowed ? "launch dinghy" : "hoist dinghy";
            if (t == coilText) return;
            coilText = t;
            Traverse.Create(coil).Field("lookText").SetValue(t);
        }

        // in-game message if the game has NotificationUi, otherwise log only
        private static void Say(string msg)
        {
            Plugin.Log.LogMessage(msg);
            if (!notifyLooked)
            {
                notifyLooked = true;
                var t = AccessTools.TypeByName("NotificationUi");
                if (t != null)
                {
                    notifyMethod = AccessTools.Method(t, "ShowNotification", new[] { typeof(string) });
                    notifyInstance = AccessTools.Field(t, "instance");
                }
                if (notifyMethod == null) Plugin.Log.LogWarning("NotificationUi not found, messages go to the log only");
            }
            if (notifyMethod == null) return;
            try
            {
                object inst = notifyMethod.IsStatic ? null : notifyInstance?.GetValue(null);
                if (notifyMethod.IsStatic || inst != null) notifyMethod.Invoke(inst, new object[] { msg });
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Notification failed: " + e.Message); }
        }

        // ---------- hoist / launch ----------
        private static void TryHoist()
        {
            Transform cur = GameState.currentBoat;
            if (cur == null || !cur.IsChildOf(boat)) { Say("Stand on Gloriana to hoist the dinghy"); return; }

            if (g.GetComponentsInChildren<GPButtonTrapdoor>().Any(t => HatchLock.IsCargoHatch(t) && t.IsOpen()))
            { Say("Close the cargo hatches first"); return; }

            Component best = null;
            float bestDist = Plugin.MaxDistance.Value;
            foreach (Object o in Object.FindObjectsOfType(dinghyType))
            {
                var d = (Component)o;
                if (cur.IsChildOf(d.transform)) continue;
                float dist = Vector3.Distance(d.transform.position, boat.position);
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            if (best == null) { Say("No dinghy alongside"); return; }

            Transform bt = best.transform;
            if (Vector3.Angle(bt.up, Vector3.up) < 5f) restOffset = bt.position.y - boat.position.y;

            try { stowMethod.Invoke(davits, new object[] { best.gameObject, false }); }
            catch (TargetInvocationException e) { Plugin.Log.LogError(e.InnerException); }
        }

        private static void Launch()
        {
            var args = launchMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? System.Activator.CreateInstance(p.ParameterType) : null).ToArray();
            try { launchMethod.Invoke(davits, args); }
            catch (TargetInvocationException e) { Plugin.Log.LogError(e.InnerException); }
        }

        // ---------- patches on Dinghies ----------
        private static void StowPostfix(Component __instance, object[] __args)
        {
            if (__instance != davits) return;

            var newStowed = Traverse.Create(__instance).Field("stowedBoat").GetValue<GameObject>();
            if (newStowed == null) { Plugin.Log.LogError("stowedBoat is null"); return; }
            if (stowed == newStowed) { ApplyPose(); return; }
            stowed = newStowed;

            GameObject real = AsGameObject(__args != null && __args.Length > 0 ? __args[0] : null)
                           ?? AsGameObject(Traverse.Create(__instance).Field("boat").GetValue());
            DisableClipInputs(real);

            Transform t = stowed.transform;
            stowedParent = t.parent;

            stowedRb = stowed.GetComponent<Rigidbody>();
            if (stowedRb != null)
            {
                if (!stowedRb.isKinematic)
                {
                    stowedRb.velocity = Vector3.zero;
                    stowedRb.angularVelocity = Vector3.zero;
                }
                stowedRb.isKinematic = true;
                oldInterp = stowedRb.interpolation;
                stowedRb.interpolation = RigidbodyInterpolation.None;
            }

            disabledComps.Clear();
            foreach (var mb in stowed.GetComponents<MonoBehaviour>())
            {
                string n = mb.GetType().Name;
                if (mb.enabled && (n == "ShiftingRigidbody" || n == "BoatAlignNormal"))
                {
                    mb.enabled = false;
                    disabledComps.Add(mb);
                }
            }

            stowedCols = stowed.GetComponentsInChildren<Collider>();
            shipCols = boat.GetComponentsInChildren<Collider>();
            SetIgnore(true);

            t.SetParent(g, true);
            origScale = t.localScale;
            ApplyPose();
            Plugin.Log.LogInfo("Dinghy stowed on deck");
        }

        private static void LaunchPrefix(Component __instance)
        {
            if (__instance != davits || stowed == null) return;

            launching = FindRealBoat(__instance);
            float xMid = S.AftX + boatLength / 2f;
            float side = ChooseSide(xMid);

            SetIgnore(false);
            Transform t = stowed.transform;
            t.localScale = origScale;
            t.SetParent(stowedParent, true);

            foreach (var c in disabledComps) if (c != null) c.enabled = true;
            disabledComps.Clear();
            foreach (var c in clipInputs) if (c != null) c.enabled = true;
            clipInputs.Clear();

            if (stowedRb != null)
            {
                stowedRb.interpolation = oldInterp;
                stowedRb.isKinematic = false;
            }

            t.position = g.TransformPoint(new Vector3(xMid, S.LaunchY, side));
            float yaw = (g.rotation * Quaternion.Euler(0f, S.Yaw, 0f)).eulerAngles.y;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (walkBox != null) walkBox.enabled = false;
            HideSupports();
            stowed = null;
            Plugin.Log.LogInfo("Dinghy launched");
        }

        private static void LaunchPostfix(Component __instance)
        {
            if (__instance != davits || launching == null) return;
            Plugin.Instance.StartCoroutine(AfterLaunch(launching));
            launching = null;
        }

        // pick the side without a quay / other boat
        private static float ChooseSide(float x)
        {
            foreach (float s in new[] { S.LaunchSide, -S.LaunchSide })
            {
                Vector3 p = g.TransformPoint(new Vector3(x, S.LaunchY + 1f, s));
                bool blocked = Physics.OverlapSphere(p, 2.5f, ~0, QueryTriggerInteraction.Ignore).Any(c =>
                    !c.transform.IsChildOf(boat) &&
                    (walk == null || !c.transform.IsChildOf(walk)) &&
                    !c.name.Contains("observer"));
                if (!blocked) return s;
            }
            return S.LaunchSide;
        }

        private static GameObject FindRealBoat(Component dav)
        {
            var go = AsGameObject(Traverse.Create(dav).Field("boat").GetValue());
            if (go != null) return go;
            foreach (var o in Resources.FindObjectsOfTypeAll(dinghyType))
            {
                var c = o as Component;
                if (c == null || !c.gameObject.scene.IsValid()) continue;
                if (Traverse.Create(c).Field("stowedBoat").GetValue<GameObject>() == stowed) return c.gameObject;
            }
            return null;
        }

        private static IEnumerator AfterLaunch(GameObject real)
        {
            yield return new WaitForFixedUpdate();
            if (real == null) yield break;

            Transform t = real.transform;
            var rb = real.GetComponent<Rigidbody>();
            Vector3 p = t.position;
            if (!float.IsNaN(restOffset)) p.y = boat.position.y + restOffset;

            Vector3 fwd = t.forward; fwd.y = 0f;
            Quaternion level = fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : t.rotation;

            t.position = p;
            t.rotation = level;
            if (rb != null && !rb.isKinematic)
            {
                rb.position = p;
                rb.rotation = level;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // ---------- water hole fix ----------
        private static GameObject AsGameObject(object o) =>
            o is GameObject go ? go : o is Component c ? c.gameObject : null;

        private static void DisableClipInputs(GameObject real)
        {
            clipInputs.Clear();
            if (real == null) return;
            foreach (var mb in real.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.enabled || !mb.GetType().Name.Contains("Clip")) continue;
                mb.enabled = false;
                clipInputs.Add(mb);
            }
        }

        // ---------- placement ----------
        private static void ApplyPose()
        {
            if (stowed == null) return;
            Transform t = stowed.transform;

            float th = Pitch();
            float tan = Mathf.Tan(th * Mathf.Deg2Rad);
            Quaternion F = Quaternion.Euler(0f, 0f, th);
            float D = S.SparD;

            t.localScale = Vector3.Scale(origScale, new Vector3(S.ScaleLength, S.ScaleHeight, S.ScaleBeam));
            t.localRotation = F * Quaternion.Euler(0f, S.Yaw, 0f);
            t.localPosition = Vector3.zero;

            Bounds b = Geo.Measure(t, g, F);
            var A = new Vector3(S.AftX, sy1 + D + (S.AftX - sx1) * tan, S.CenterZ);
            Vector3 a = Quaternion.Inverse(F) * A;
            var want = new Vector3(a.x + b.size.x / 2f, a.y + S.KeelOffset + b.size.y / 2f, a.z);
            t.localPosition = F * (want - b.center);

            b = Geo.Measure(t, g, F);
            boatLength = b.size.x / S.ScaleLength;
            Bounds ship = Geo.Measure(t, g, Quaternion.identity);
            UpdateWalkBox(b, F);
            Plugin.Log.LogInfo("Dinghy pose: " + PlaceSupports(ship, tan));
        }

        // ---------- timbers, chocks, lashings ----------
        private static string PlaceSupports(Bounds ship, float tan)
        {
            EnsureSpars();
            Physics.SyncTransforms();
            if (walkBox != null) walkBox.enabled = false;   // keep it out of the deck raycasts

            float D = S.SparD;
            float hx = S.ChockSizeX / 2f, hz = S.ChockSizeZ / 2f;
            var info = new StringBuilder();
            int ci = 0;
            float[] frac = { S.SparAft, S.SparFore };

            for (int i = 0; i < 2; i++)
            {
                float x = ship.min.x + ship.size.x * frac[i];
                float fallback = sy1 + D + (x - sx1) * tan + S.KeelOffset;
                float top = KeelBottom(x, fallback);
                float bottom = top - D;
                float half = S.SparLength / 2f;

                info.Append($"timber{i} x={x:F2}");
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = ChockZ(x, side, bottom, hx, hz);
                    if (!SurfaceAt(x, z, bottom, out float deck, out _)) continue;
                    float gap = bottom - deck;
                    if (gap <= 0.005f) continue;

                    float h = gap + S.ChockSink + 0.01f;   // into the surface below and slightly into the timber
                    var c = Chock(ci++).transform;
                    c.localPosition = new Vector3(x, deck - S.ChockSink + h / 2f, z);
                    c.localRotation = Quaternion.identity;
                    c.localScale = new Vector3(S.ChockSizeX, h, S.ChockSizeZ);
                    c.gameObject.SetActive(true);
                    half = Mathf.Max(half, Mathf.Abs(z - S.CenterZ) + hz + 0.04f);
                    info.Append($" chock z={z:F2} h={gap:F2}");
                }
                info.Append("; ");

                var s = spars[i].transform;
                s.localPosition = new Vector3(x, top - D / 2f, S.CenterZ);
                s.localRotation = Quaternion.identity;
                s.localScale = new Vector3(D, D, half * 2f);
                spars[i].SetActive(true);

                PlaceLashing(i, x, top - D / 2f, half);
            }
            for (int k = ci; k < chocks.Count; k++) chocks[k].SetActive(false);
            if (walkBox != null) walkBox.enabled = true;
            return info.ToString();
        }

        // move the chock outwards until it stands fully on the hatch frame (flat, not on the grating)
        private static float ChockZ(float x, float side, float fromY, float hx, float hz)
        {
            float z0 = S.CenterZ + side * (S.SparLength / 2f - S.ChockInset);
            for (float d = 0f; d <= 0.6f; d += 0.02f)
            {
                float z = z0 + side * d;
                if (!GoodFooting(x, z, fromY, hx, hz)) continue;
                // step a bit further from the grating edge while still on the frame
                for (int k = 2; k >= 1; k--)
                {
                    float z2 = z + side * 0.02f * k;
                    if (GoodFooting(x, z2, fromY, hx, hz)) return z2;
                }
                return z;
            }
            return z0;
        }

        private static bool GoodFooting(float x, float z, float fromY, float hx, float hz)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    if (!SurfaceAt(x + ix * hx, z + iz * hz, fromY, out float y, out bool hatch) || hatch) return false;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
            return max - min < 0.02f;
        }

        // highest walkable surface under a point; onHatch = it is a hatch grating
        private static bool SurfaceAt(float x, float z, float fromY, out float y, out bool onHatch)
        {
            y = fromY; onHatch = false;
            if (walk == null) return false;
            Vector3 o = walk.TransformPoint(new Vector3(x, fromY + 0.15f, z));
            float best = float.MinValue;
            Collider bc = null;
            foreach (var h in Physics.RaycastAll(o, walk.TransformDirection(Vector3.down), 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == walkBox || !h.collider.transform.IsChildOf(walk)) continue;
                float hy = walk.InverseTransformPoint(h.point).y;
                if (hy > best) { best = hy; bc = h.collider; }
            }
            if (bc == null) return false;
            y = best;
            onHatch = bc.name == "trapdoor";
            return true;
        }

        private static void PlaceLashing(int i, float x, float endY, float half)
        {
            var lr = Lashing(i);
            float h = half - S.LashInset;
            float cz = S.CenterZ;
            float lift = S.LashClearance + S.LashWidth / 2f;

            var pts = new List<Vector3> { new Vector3(x, endY, cz - h) };
            for (float z = cz - h; z <= cz + h + 0.001f; z += 0.04f)
            {
                float top = HullTop(x, z, endY);
                if (top > float.MinValue) pts.Add(new Vector3(x, top + lift, z));
            }
            pts.Add(new Vector3(x, endY, cz + h));

            if (pts.Count < 3) { lr.gameObject.SetActive(false); return; }
            lr.widthMultiplier = S.LashWidth;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.gameObject.SetActive(true);
        }

        private static float HullTop(float x, float z, float fromY)
        {
            Vector3 o = g.TransformPoint(new Vector3(x, fromY + 4f, z));
            float best = float.MinValue;
            foreach (var h in Physics.RaycastAll(o, -g.up, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(h.collider is MeshCollider) || !h.collider.transform.IsChildOf(stowed.transform)) continue;
                float y = g.InverseTransformPoint(h.point).y;
                if (y > best) best = y;
            }
            return best;
        }

        private static float KeelBottom(float x, float fallback)
        {
            float best = float.MaxValue;
            for (float dz = -0.6f; dz <= 0.61f; dz += 0.3f)
            {
                Vector3 o = g.TransformPoint(new Vector3(x, fallback - 1f, S.CenterZ + dz));
                foreach (var h in Physics.RaycastAll(o, g.up, 4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!(h.collider is MeshCollider) || h.collider.name == "tarp") continue;
                    if (!h.collider.transform.IsChildOf(stowed.transform)) continue;
                    float y = g.InverseTransformPoint(h.point).y;
                    if (y < best) best = y;
                }
            }
            return best == float.MaxValue ? fallback : best;
        }

        private static LineRenderer Lashing(int i)
        {
            if (lashings[i] != null) return lashings[i];
            var go = new GameObject("dinghy_lashing_" + i);
            go.transform.SetParent(g, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            var src = g.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(l => l != lr && l.sharedMaterial != null);
            if (src != null) { lr.sharedMaterial = src.sharedMaterial; lr.textureMode = src.textureMode; }
            else lr.sharedMaterial = WoodMat();
            lashings[i] = lr;
            return lr;
        }

        private static Material WoodMat()
        {
            if (woodMat != null) return woodMat;
            var src = g.Find(S.MaterialSource);
            var r = src != null ? src.GetComponent<Renderer>() : null;
            woodMat = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Standard"));
            woodMat.name = "dinghy_wood";
            foreach (var name in woodMat.GetTexturePropertyNames())
            {
                woodMat.SetTextureScale(name, new Vector2(S.TileU, S.TileV));
                woodMat.SetTextureOffset(name, new Vector2(S.OffsetU, S.OffsetV));
            }
            return woodMat;
        }

        private static GameObject NewPart(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(g, false);
            go.GetComponent<Renderer>().sharedMaterial = WoodMat();
            return go;
        }

        private static void EnsureSpars()
        {
            for (int i = 0; i < 2; i++)
                if (spars[i] == null) spars[i] = NewPart("dinghy_timber_" + i);
        }

        private static GameObject Chock(int i)
        {
            while (chocks.Count <= i) chocks.Add(NewPart("dinghy_chock_" + chocks.Count));
            return chocks[i];
        }

        private static void HideSupports()
        {
            foreach (var s in spars) if (s != null) s.SetActive(false);
            foreach (var c in chocks) if (c != null) c.SetActive(false);
            foreach (var l in lashings) if (l != null) l.gameObject.SetActive(false);
        }

        private static void UpdateWalkBox(Bounds b, Quaternion F)
        {
            if (walk == null) return;
            if (walkBox == null)
            {
                var go = new GameObject("dinghy_on_deck");
                go.transform.SetParent(walk, false);
                var any = walk.GetComponentInChildren<Collider>();
                if (any != null) go.layer = any.gameObject.layer;
                walkBox = go.AddComponent<BoxCollider>();
            }
            walkBox.transform.localPosition = Vector3.zero;
            walkBox.transform.localRotation = F;
            walkBox.center = b.center;
            walkBox.size = b.size;
            walkBox.enabled = true;
        }

        private static void SetIgnore(bool ignore)
        {
            if (stowedCols == null || shipCols == null) return;
            foreach (var a in stowedCols)
                foreach (var c in shipCols)
                    if (a != null && c != null) Physics.IgnoreCollision(a, c, ignore);
        }
    }
}