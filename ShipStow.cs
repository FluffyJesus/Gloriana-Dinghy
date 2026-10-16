using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OldChronianDinghy
{
    // Deck stowage of a Dinghies cutter on one ship (one instance per ShipProfile).
    // WALK colliders use the same local axes as the ship model.
    internal sealed class ShipStow
    {
        internal static readonly List<ShipStow> All = new List<ShipStow>();
        private const string CoilName = "dinghy_hoist_coil";

        private static System.Type davitsType, dinghyType;
        private static MethodInfo stowMethod, launchMethod;
        private static bool notifyLooked;
        private static MethodInfo notifyMethod;
        private static FieldInfo notifyInstance;

        public readonly ShipProfile P;
        public string LastInfo = "", CoilInfo = "";

        private Component davits;
        private Transform boat, g, walk;
        private float nextTry;

        private float sx1 = 2.15f, sy1 = 1.30f, sx2 = 5.15f, sy2 = 1.40f;   // hatch tops (HatchSpars)
        private readonly List<float> px = new List<float>();                 // plank x (DeckPlanks)
        private readonly List<GameObject> spars = new List<GameObject>();
        private readonly List<GameObject> chocks = new List<GameObject>();
        private readonly List<GameObject> posts = new List<GameObject>();
        private readonly List<GameObject> beams = new List<GameObject>();
        private readonly List<LineRenderer> lashings = new List<LineRenderer>();
        private readonly List<BoxCollider> walkParts = new List<BoxCollider>();
        private Material woodMat;
        private HoistCoil coil;

        private GameObject stowed;
        private Transform stowedParent;
        private Vector3 origScale = Vector3.one;
        private Rigidbody stowedRb;
        private RigidbodyInterpolation oldInterp;
        private readonly List<Behaviour> disabledComps = new List<Behaviour>();
        private readonly List<Behaviour> clipInputs = new List<Behaviour>();
        private Collider[] stowedCols, shipCols;
        private BoxCollider walkBox;
        private float launchX;
        private GameObject launching;
        private float restOffset = float.NaN;

        public ShipStow(ShipProfile p) { P = p; launchX = p.StowX; }

        public bool IsStowed => stowed != null;
        private bool Ready => davits != null;
        private bool PlayerAboard
        {
            get { var cur = GameState.currentBoat; return boat != null && cur != null && cur.IsChildOf(boat); }
        }

        public static void TickAll()
        {
            foreach (var s in All)
            {
                if (!s.Ready) { s.TryInit(); continue; }
                if (s.coil != null) s.coil.lookText = s.IsStowed ? "launch dinghy" : "hoist dinghy";
            }
            var key = Plugin.HoistKey.Value;
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return;
            var ship = All.FirstOrDefault(s => s.Ready && s.PlayerAboard);
            if (ship != null) ship.Toggle();
            else Say("Stand on a ship with dinghy stowage");
        }

        public void Toggle()
        {
            if (!Ready) return;
            if (IsStowed) Launch();
            else TryHoist();
        }

        // called by the tuning window
        public void Retune(bool coilChanged)
        {
            if (!Ready) return;
            if (woodMat != null) TintWood(woodMat);
            if (coilChanged) RebuildCoil();
            ApplyPose();
        }

        // ---------- setup ----------
        private void TryInit()
        {
            if (Time.time < nextTry) return;
            nextTry = Time.time + 2f;

            if (davitsType == null) davitsType = AccessTools.TypeByName("DinghiesScripts.Davits");
            if (dinghyType == null) dinghyType = AccessTools.TypeByName("DinghiesScripts.Dinghy");
            if (davitsType == null || dinghyType == null) return;

            var refs = Object.FindObjectsOfType<BoatRefs>().FirstOrDefault(r => r.name.StartsWith(P.BoatPrefix));
            if (refs == null) return;
            boat = refs.transform;
            g = refs.boatModel;
            walk = refs.walkCol;
            ResetState();

            string marker = P.Id + "dinghy_applied";
            if (P.Tweaks != null && g.Find(marker) == null)   // never apply twice
            {
                P.Tweaks(g, walk);
                new GameObject(marker).transform.SetParent(g, false);
            }
            if (P.HatchNames.Length > 0) MeasureHatches();
            PatchDinghies();

            var old = g.Find(P.DavitsName);
            if (old != null) davits = old.GetComponent(davitsType);
            if (davits == null)
            {
                var go = new GameObject(P.DavitsName);
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

            var c = g.Find(CoilName);
            if (c == null) CreateCoil();
            else { coil = c.GetComponent<HoistCoil>(); if (coil != null) coil.Owner = this; }
            Plugin.Log.LogInfo("Deck stowage ready on " + boat.name);
        }

        private void ResetState()
        {
            spars.Clear(); chocks.Clear(); posts.Clear(); beams.Clear(); lashings.Clear(); walkParts.Clear(); px.Clear();
            woodMat = null; walkBox = null; coil = null; CoilInfo = "";
            stowed = null; stowedRb = null; stowedCols = null; shipCols = null;
            disabledComps.Clear(); clipInputs.Clear();
            launchX = P.StowX;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var c = new GameObject(name);
            c.transform.SetParent(parent.transform, false);
            return c;
        }

        private static void PatchDinghies()
        {
            if (stowMethod != null) return;
            stowMethod = AccessTools.Method(davitsType, "Stow");
            launchMethod = AccessTools.Method(davitsType, "Launch");
            Plugin.H.Patch(stowMethod,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ShipStow), nameof(StowPostfix))));
            Plugin.H.Patch(launchMethod,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(ShipStow), nameof(LaunchPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ShipStow), nameof(LaunchPostfix))));
        }

        private void MeasureHatches()
        {
            var gr = g.GetComponentsInChildren<GPButtonTrapdoor>(true)
                      .Where(t => HatchLock.IsCargoHatch(P, t))
                      .Select(t => Geo.Measure(t.transform, g, Quaternion.identity))
                      .OrderBy(b => b.center.x)
                      .ToList();
            if (gr.Count < 2) return;
            Bounds a = gr[0], b = gr[gr.Count - 1];
            sx1 = a.min.x + a.size.x * 0.25f; sy1 = a.max.y;
            sx2 = b.max.x - b.size.x * 0.25f; sy2 = b.max.y;
        }

        private float Pitch() => Mathf.Atan2(sy2 - sy1, sx2 - sx1) * Mathf.Rad2Deg;

        // ---------- coil ----------
        private void CreateCoil()
        {
            var src = g.Find("0_CAPSTAN/coiled_rope");
            var mf = src != null ? src.GetComponent<MeshFilter>() : null;
            var mr = src != null ? src.GetComponent<MeshRenderer>() : null;
            if (mf == null || mr == null) { Plugin.Log.LogWarning("Coil model not found"); return; }

            var go = new GameObject(CoilName);
            go.transform.SetParent(g, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;

            // lay the coil flat: its thinnest mesh axis -> ship up
            Vector3 ext = mf.sharedMesh.bounds.extents;
            Vector3 thin = ext.x <= ext.y && ext.x <= ext.z ? Vector3.right
                         : ext.y <= ext.z ? Vector3.up : Vector3.forward;
            Quaternion r0 = Quaternion.Inverse(g.rotation) * src.rotation;
            Vector3 axis = r0 * thin;
            if (axis.y < 0f) axis = -axis;
            Quaternion flat = Quaternion.FromToRotation(axis, Vector3.up) * r0;
            go.transform.localRotation = flat;

            Bounds b = Geo.Measure(go.transform, g, Quaternion.identity);
            go.transform.localScale = Vector3.one * (P.CoilDiameter / Mathf.Max(b.size.x, b.size.z, 0.01f));

            float x = P.CoilX;
            if (P.CoilFrom != null)
            {
                var from = g.Find(P.CoilFrom);
                if (from != null) x = Geo.Measure(from, g, Quaternion.identity).min.x - P.CoilX;
            }
            float z = P.CoilZ;
            var deckCol = SurfaceHit(x, z, P.CoilProbeY, out float y0);
            if (deckCol == null) y0 = P.CoilY;
            CoilInfo = deckCol != null ? $"coil on '{deckCol.name}' y={y0:F3}" : $"coil: no surface, CoilY={y0:F3}";

            // local deck plane from 4 samples around the coil (outliers like rails are ignored)
            float r = P.CoilDiameter * 0.45f;
            float H(float qx, float qz) =>
                SurfaceAt(qx, qz, y0 + 0.3f, out float y, out _) && Mathf.Abs(y - y0) < 0.1f ? y : y0;
            float slopeX = (H(x + r, z) - H(x - r, z)) / (2f * r);
            float slopeZ = (H(x, z + r) - H(x, z - r)) / (2f * r);
            Vector3 n = new Vector3(-slopeX, 1f, -slopeZ).normalized;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, n);
            go.transform.localRotation = tilt * flat;

            b = Geo.Measure(go.transform, g, tilt);
            Vector3 dp = Quaternion.Inverse(tilt) * new Vector3(x, y0, z);
            go.transform.localPosition += tilt * new Vector3(dp.x - b.center.x, dp.y - P.CoilSink - b.min.y, dp.z - b.center.z);
            Plugin.Log.LogInfo($"Coil on {P.Id}: {CoilInfo}, slope x {slopeX:F3}, z {slopeZ:F3}");

            go.AddComponent<BoxCollider>();
            coil = go.AddComponent<HoistCoil>();   // its Start() adds the Outline itself
            coil.Owner = this;
        }

        private void RebuildCoil()
        {
            var old = g.Find(CoilName);
            if (old != null) { old.name = CoilName + "_old"; Object.Destroy(old.gameObject); }
            coil = null;
            CreateCoil();
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
        private void TryHoist()
        {
            if (!PlayerAboard) { Say("Stand on " + P.DisplayName + " to hoist the dinghy"); return; }
            Transform cur = GameState.currentBoat;

            if (P.HatchNames.Length > 0 &&
                g.GetComponentsInChildren<GPButtonTrapdoor>().Any(t => HatchLock.IsCargoHatch(P, t) && t.IsOpen()))
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

        private void Launch()
        {
            var args = launchMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? System.Activator.CreateInstance(p.ParameterType) : null).ToArray();
            try { launchMethod.Invoke(davits, args); }
            catch (TargetInvocationException e) { Plugin.Log.LogError(e.InnerException); }
        }

        // ---------- patches on Dinghies ----------
        private static ShipStow Of(Component c) => c == null ? null : All.FirstOrDefault(s => s.davits == c);
        private static void StowPostfix(Component __instance, object[] __args) => Of(__instance)?.OnStowed(__args);
        private static void LaunchPrefix(Component __instance) => Of(__instance)?.OnLaunching();
        private static void LaunchPostfix(Component __instance) => Of(__instance)?.OnLaunched();

        private void OnStowed(object[] args)
        {
            var newStowed = Traverse.Create(davits).Field("stowedBoat").GetValue<GameObject>();
            if (newStowed == null) { Plugin.Log.LogError("stowedBoat is null"); return; }
            if (stowed == newStowed) { ApplyPose(); return; }
            stowed = newStowed;

            GameObject real = AsGameObject(args != null && args.Length > 0 ? args[0] : null)
                           ?? AsGameObject(Traverse.Create(davits).Field("boat").GetValue());
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
            shipCols = boat.GetComponentsInChildren<Collider>();   // before parenting
            SetIgnore(true);

            t.SetParent(g, true);
            origScale = t.localScale;
            ApplyPose();
            Plugin.Log.LogInfo("Dinghy stowed on " + P.DisplayName);
        }

        private void OnLaunching()
        {
            if (stowed == null) return;

            launching = FindRealBoat();
            float side = ChooseSide(launchX);

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

            t.position = g.TransformPoint(new Vector3(launchX, P.LaunchY, side));
            float yaw = (g.rotation * Quaternion.Euler(0f, P.Yaw, 0f)).eulerAngles.y;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (walkBox != null) walkBox.enabled = false;
            HideSupports();
            stowed = null;
            Plugin.Log.LogInfo("Dinghy launched from " + P.DisplayName);
        }

        private void OnLaunched()
        {
            if (launching == null) return;
            Plugin.Instance.StartCoroutine(AfterLaunch(launching));
            launching = null;
        }

        // pick the side without a quay / other boat
        private float ChooseSide(float x)
        {
            foreach (float s in new[] { P.LaunchSide, -P.LaunchSide })
            {
                Vector3 p = g.TransformPoint(new Vector3(x, P.LaunchY + 1f, s));
                bool blocked = Physics.OverlapSphere(p, 2.5f, ~0, QueryTriggerInteraction.Ignore).Any(c =>
                    !c.transform.IsChildOf(boat) &&
                    (walk == null || !c.transform.IsChildOf(walk)) &&
                    !c.name.Contains("observer"));
                if (!blocked) return s;
            }
            return P.LaunchSide;
        }

        private GameObject FindRealBoat()
        {
            var go = AsGameObject(Traverse.Create(davits).Field("boat").GetValue());
            if (go != null) return go;
            foreach (var o in Resources.FindObjectsOfTypeAll(dinghyType))
            {
                var c = o as Component;
                if (c == null || !c.gameObject.scene.IsValid()) continue;
                if (Traverse.Create(c).Field("stowedBoat").GetValue<GameObject>() == stowed) return c.gameObject;
            }
            return null;
        }

        private IEnumerator AfterLaunch(GameObject real)
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

        private void DisableClipInputs(GameObject real)
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
        private void ApplyPose()
        {
            if (stowed == null) return;
            Transform t = stowed.transform;
            t.localScale = Vector3.Scale(origScale, new Vector3(P.ScaleLength, P.ScaleHeight, P.ScaleBeam));

            t.localRotation = Quaternion.Euler(0f, P.Yaw, 0f);
            t.localPosition = Vector3.zero;
            float len = Geo.Measure(t, g, Quaternion.identity).size.x;
            float aft = P.StowXIsCenter ? P.StowX - len / 2f : P.StowX;

            // reference line through the tops of the supports
            float th, x1, y1;
            if (P.Supports == SupportKind.HatchSpars) { th = Pitch(); x1 = sx1; y1 = sy1 + P.SparD; }
            else { MeasurePlanks(aft, len); th = 0f; x1 = aft; y1 = PlankTop; }

            float tan = Mathf.Tan(th * Mathf.Deg2Rad);
            Quaternion F = Quaternion.Euler(0f, 0f, th);
            t.localRotation = F * Quaternion.Euler(0f, P.Yaw, 0f);
            t.localPosition = Vector3.zero;

            Bounds b = Geo.Measure(t, g, F);
            var A = new Vector3(aft, y1 + (aft - x1) * tan, P.CenterZ);
            Vector3 a = Quaternion.Inverse(F) * A;
            var want = new Vector3(a.x + b.size.x / 2f, a.y + P.KeelOffset + b.size.y / 2f, a.z);
            t.localPosition = F * (want - b.center);

            string info = P.Supports == SupportKind.DeckPlanks ? PlacePlanks() : "";

            b = Geo.Measure(t, g, F);
            launchX = P.StowXIsCenter ? P.StowX : P.StowX + b.size.x / P.ScaleLength / 2f;
            UpdateWalkBox(b, F);
            if (P.Supports == SupportKind.HatchSpars)
                info = PlaceSpars(Geo.Measure(t, g, Quaternion.identity), tan);

            LastInfo = info;
            Plugin.Log.LogInfo("Dinghy pose (" + P.Id + "): " + info);
        }

        // ---------- DeckPlanks ----------
        private float PlankTop => P.PlankY + P.PlankT;   // PlankY = underside of the planks

        private void MeasurePlanks(float aft, float len)
        {
            px.Clear();
            for (int i = 0; i < P.PlankCount; i++)
            {
                float f = P.PlankCount == 1 ? 0.5f : Mathf.Lerp(P.PlankAft, P.PlankFore, i / (float)(P.PlankCount - 1));
                px.Add(aft + len * f);
            }
        }

        private string PlacePlanks()
        {
            Transform t = stowed.transform;
            float top = PlankTop, under = P.PlankY;
            Physics.SyncTransforms();

            // KeelOffset = keel height above the nearest plank; the dinghy rests on the bilge chocks
            float min = float.MaxValue;
            foreach (float x in px) min = Mathf.Min(min, KeelBottom(x, top) - top);
            t.localPosition += Vector3.up * (P.KeelOffset - min);
            Physics.SyncTransforms();

            var info = new StringBuilder();
            int ci = 0, pi = 0, wi = 0;

            // planks, bilge chocks, lashings
            for (int i = 0; i < px.Count; i++)
            {
                float x = px[i];
                PlaceBox(Spar(i), new Vector3(x, top - P.PlankT / 2f, 0f), new Vector3(P.PlankW, P.PlankT, P.PlankZ * 2f), wi++);
                info.Append($"plank{i} x={x:F2}");

                foreach (float side in new[] { -1f, 1f })
                {
                    float z = P.CenterZ + side * P.BilgeZ;
                    float h = HullBottom(x, z, top) - top;
                    if (h < 0.01f || h > P.ChockMaxH) continue;
                    var c = Chock(ci++).transform;
                    c.localPosition = new Vector3(x, top + (h + 0.01f) / 2f, z);   // 1 cm into the hull
                    c.localRotation = Quaternion.identity;
                    c.localScale = new Vector3(P.ChockSizeX, h + 0.01f, P.ChockSizeZ);
                    c.gameObject.SetActive(true);
                    info.Append($" chock h={h:F2}");
                }
                info.Append("; ");
                PlankLashing(i, x, top);
            }

            // beams along the gunwales under the plank ends, posts down to the rail if there is a gap
            float xa = px[0] - P.PlankW / 2f - P.BeamEnd, xb = px[px.Count - 1] + P.PlankW / 2f + P.BeamEnd;
            float beamZ = P.PlankZ - P.BeamOver - P.BeamW / 2f;
            float beamBottom = under - P.BeamT;
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1f : 1f, z = side * beamZ;
                PlaceBox(Beam(k), new Vector3((xa + xb) / 2f, under - P.BeamT / 2f, z),
                         new Vector3(xb - xa, P.BeamT, P.BeamW), wi++);
                info.Append(side < 0f ? "beam L" : "beam R");

                foreach (float x in px)
                {
                    if (!SurfaceAt(x, z, beamBottom - 0.14f, out float deck, out _)) { info.Append(" no rail"); continue; }
                    float gap = beamBottom - deck;
                    info.Append($" gap={gap:F3}");
                    if (gap < 0.01f) continue;
                    if (gap > P.PostMaxH) { info.Append(" (skipped)"); continue; }   // ray went past the gangway down to the deck
                    float h = gap + P.PostSink + 0.01f;   // into the rail and 1 cm into the beam
                    PlaceBox(Post(pi++), new Vector3(x, deck - P.PostSink + h / 2f, z),
                             new Vector3(P.PlankW + 2f * P.PostOver, h, P.BeamW), wi++);
                }
                info.Append("; ");
            }

            HideFrom(chocks, ci);
            HideFrom(posts, pi);
            HideFrom(spars, px.Count);
            for (int k = wi; k < walkParts.Count; k++) walkParts[k].enabled = false;
            return info.ToString();
        }

        private void PlaceBox(GameObject go, Vector3 center, Vector3 size, int walkIndex)
        {
            var tr = go.transform;
            tr.localPosition = center;
            tr.localRotation = Quaternion.identity;
            tr.localScale = size;
            go.SetActive(true);
            PlaceWalkPart(walkIndex, center, size);
        }

        // taut rope: upper convex hull of the dinghy cross-section (tarp included) plus the two plank anchors
        private void PlankLashing(int i, float x, float top)
        {
            var lr = Lashing(i);
            float c = P.LashClearance + P.LashWidth / 2f;
            var pts = new List<Vector2>();   // (z, y)
            float yTop = float.MinValue;

            for (float z = P.CenterZ - 2.5f; z <= P.CenterZ + 2.5f; z += 0.04f)
            {
                float h = HullTop(x, z, top);
                if (h == float.MinValue) continue;
                pts.Add(new Vector2(z, h + c));
                yTop = Mathf.Max(yTop, h);
            }
            if (pts.Count < 2) { lr.gameObject.SetActive(false); return; }

            for (float y = top; y <= yTop; y += 0.04f)
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = HullSide(x, y, side);
                    if (!float.IsNaN(z)) pts.Add(new Vector2(z + side * c, y));
                }

            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var p in pts) { zMin = Mathf.Min(zMin, p.x); zMax = Mathf.Max(zMax, p.x); }
            float y0 = top + P.LashWidth / 2f;
            pts.Add(new Vector2(zMin - P.LashOut, y0));
            pts.Add(new Vector2(zMax + P.LashOut, y0));

            SetLine(lr, UpperHull(pts).Select(p => new Vector3(x, p.y, p.x)).ToList());
        }

        // outermost point of the dinghy at height y, seen from one side
        private float HullSide(float x, float y, float side)
        {
            Vector3 o = g.TransformPoint(new Vector3(x, y, P.CenterZ + side * 3f));
            Vector3 d = g.TransformDirection(new Vector3(0f, 0f, -side));
            float best = float.NaN, bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(o, d, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(h.collider is MeshCollider) || !h.collider.transform.IsChildOf(stowed.transform)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = g.InverseTransformPoint(h.point).z; }
            }
            return best;
        }

        private static List<Vector2> UpperHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            foreach (var p in pts)
            {
                while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) >= 0f) h.RemoveAt(h.Count - 1);
                h.Add(p);
            }
            return h;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        private float HullBottom(float x, float z, float fromY)
        {
            Vector3 o = g.TransformPoint(new Vector3(x, fromY - 0.2f, z));
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(o, g.up, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(h.collider is MeshCollider) || h.collider.name == "tarp") continue;
                if (!h.collider.transform.IsChildOf(stowed.transform)) continue;
                best = Mathf.Min(best, g.InverseTransformPoint(h.point).y);
            }
            return best;
        }

        // ---------- HatchSpars (Gloriana) ----------
        private string PlaceSpars(Bounds ship, float tan)
        {
            Physics.SyncTransforms();
            if (walkBox != null) walkBox.enabled = false;   // keep it out of the deck raycasts

            float D = P.SparD;
            float hx = P.ChockSizeX / 2f, hz = P.ChockSizeZ / 2f;
            var info = new StringBuilder();
            int ci = 0;
            float[] frac = { P.SparAft, P.SparFore };

            for (int i = 0; i < 2; i++)
            {
                float x = ship.min.x + ship.size.x * frac[i];
                float fallback = sy1 + D + (x - sx1) * tan + P.KeelOffset;
                float top = KeelBottom(x, fallback);
                float bottom = top - D;
                float half = P.SparLength / 2f;

                info.Append($"timber{i} x={x:F2}");
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = ChockZ(x, side, bottom, hx, hz);
                    if (!SurfaceAt(x, z, bottom, out float deck, out _)) continue;
                    float gap = bottom - deck;
                    if (gap <= 0.005f) continue;

                    float h = gap + P.ChockSink + 0.01f;   // into the surface below and slightly into the timber
                    var c = Chock(ci++).transform;
                    c.localPosition = new Vector3(x, deck - P.ChockSink + h / 2f, z);
                    c.localRotation = Quaternion.identity;
                    c.localScale = new Vector3(P.ChockSizeX, h, P.ChockSizeZ);
                    c.gameObject.SetActive(true);
                    half = Mathf.Max(half, Mathf.Abs(z - P.CenterZ) + hz + 0.04f);
                    info.Append($" chock z={z:F2} h={gap:F2}");
                }
                info.Append("; ");

                var s = Spar(i).transform;
                s.localPosition = new Vector3(x, top - D / 2f, P.CenterZ);
                s.localRotation = Quaternion.identity;
                s.localScale = new Vector3(D, D, half * 2f);
                s.gameObject.SetActive(true);

                float lh = half - P.LashInset;
                SpanLashing(i, x, top - D / 2f, P.CenterZ - lh, P.CenterZ + lh);
            }
            HideFrom(chocks, ci);
            if (walkBox != null) walkBox.enabled = true;
            return info.ToString();
        }

        // move the chock outwards until it stands fully on the hatch frame (flat, not on the grating)
        private float ChockZ(float x, float side, float fromY, float hx, float hz)
        {
            float z0 = P.CenterZ + side * (P.SparLength / 2f - P.ChockInset);
            for (float d = 0f; d <= 0.6f; d += 0.02f)
            {
                float z = z0 + side * d;
                if (!GoodFooting(x, z, fromY, hx, hz)) continue;
                for (int k = 2; k >= 1; k--)
                {
                    float z2 = z + side * 0.02f * k;
                    if (GoodFooting(x, z2, fromY, hx, hz)) return z2;
                }
                return z;
            }
            return z0;
        }

        private bool GoodFooting(float x, float z, float fromY, float hx, float hz)
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

        private void SpanLashing(int i, float x, float endY, float zA, float zB)
        {
            float lift = P.LashClearance + P.LashWidth / 2f;
            var pts = new List<Vector3> { new Vector3(x, endY, zA) };
            for (float z = zA; z <= zB + 0.001f; z += 0.04f)
            {
                float top = HullTop(x, z, endY);
                if (top > float.MinValue) pts.Add(new Vector3(x, top + lift, z));
            }
            pts.Add(new Vector3(x, endY, zB));
            var lr = Lashing(i);
            if (pts.Count < 3) { lr.gameObject.SetActive(false); return; }
            SetLine(lr, pts);
        }

        // ---------- shared geometry ----------
        // highest walkable surface under a point (model coords); onHatch = it is a hatch grating
        private bool SurfaceAt(float x, float z, float fromY, out float y, out bool onHatch)
        {
            var c = SurfaceHit(x, z, fromY, out y);
            onHatch = c != null && c.name == "trapdoor";
            return c != null;
        }

        private Collider SurfaceHit(float x, float z, float fromY, out float y)
        {
            y = fromY;
            if (walk == null) return null;
            Vector3 o = walk.TransformPoint(new Vector3(x, fromY + 0.15f, z));
            float best = float.MinValue;
            Collider bc = null;
            foreach (var h in Physics.RaycastAll(o, -walk.up, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.name.StartsWith("dinghy_") || !h.collider.transform.IsChildOf(walk)) continue;   // our own boxes
                float hy = walk.InverseTransformPoint(h.point).y;
                if (hy > best) { best = hy; bc = h.collider; }
            }
            if (bc != null) y = best;
            return bc;
        }

        private float HullTop(float x, float z, float fromY)
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

        private float KeelBottom(float x, float fallback)
        {
            float best = float.MaxValue;
            for (float dz = -0.6f; dz <= 0.61f; dz += 0.3f)
            {
                Vector3 o = g.TransformPoint(new Vector3(x, fallback - 1f, P.CenterZ + dz));
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

        // ---------- parts ----------
        private void SetLine(LineRenderer lr, List<Vector3> pts)
        {
            lr.widthMultiplier = P.LashWidth;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.gameObject.SetActive(true);
        }

        private LineRenderer Lashing(int i)
        {
            while (lashings.Count <= i)
            {
                var go = new GameObject("dinghy_lashing_" + lashings.Count);
                go.transform.SetParent(g, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.numCornerVertices = 2;
                lr.numCapVertices = 2;
                var src = g.GetComponentsInChildren<LineRenderer>(true)
                           .FirstOrDefault(l => l != lr && l.sharedMaterial != null && !l.name.StartsWith("dinghy_"));
                if (src != null) { lr.sharedMaterial = src.sharedMaterial; lr.textureMode = src.textureMode; }
                else lr.sharedMaterial = WoodMat();
                lashings.Add(lr);
            }
            return lashings[i];
        }

        private Material WoodMat()
        {
            if (woodMat != null) return woodMat;
            var src = g.Find(P.MaterialSource);
            var r = src != null ? src.GetComponent<Renderer>() : null;
            woodMat = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Standard"));
            woodMat.name = "dinghy_wood";
            foreach (var name in woodMat.GetTexturePropertyNames())
            {
                if (P.WoodFlat) { woodMat.SetTexture(name, null); continue; }   // plain colour, same shader
                woodMat.SetTextureScale(name, new Vector2(P.TileU, P.TileV));
                woodMat.SetTextureOffset(name, Vector2.zero);
            }
            TintWood(woodMat);
            return woodMat;
        }

        private void TintWood(Material m)
        {
            if (P.WoodFlat && m.HasProperty("_Color")) m.color = new Color(P.WoodR, P.WoodG, P.WoodB);
        }

        private GameObject NewPart(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(g, false);
            go.GetComponent<Renderer>().sharedMaterial = WoodMat();
            return go;
        }

        private static GameObject Part(List<GameObject> list, int i, System.Func<string, GameObject> make, string prefix)
        {
            while (list.Count <= i) list.Add(make(prefix + list.Count));
            return list[i];
        }

        private GameObject Spar(int i) => Part(spars, i, NewPart, "dinghy_timber_");
        private GameObject Chock(int i) => Part(chocks, i, NewPart, "dinghy_chock_");
        private GameObject Post(int i) => Part(posts, i, NewPart, "dinghy_post_");
        private GameObject Beam(int i) => Part(beams, i, NewPart, "dinghy_beam_");

        // walk-space copy of a plank / beam / post so the player collides with it
        private void PlaceWalkPart(int i, Vector3 center, Vector3 size)
        {
            if (walk == null) return;
            while (walkParts.Count <= i)
            {
                var go = new GameObject("dinghy_part_col_" + walkParts.Count);
                go.transform.SetParent(walk, false);
                var any = walk.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.name.StartsWith("dinghy_"));
                if (any != null) go.layer = any.gameObject.layer;
                walkParts.Add(go.AddComponent<BoxCollider>());
            }
            var bc = walkParts[i];
            bc.transform.localPosition = center;
            bc.transform.localRotation = Quaternion.identity;
            bc.center = Vector3.zero;
            bc.size = size;
            bc.enabled = true;
        }

        private static void HideFrom(List<GameObject> list, int start)
        {
            for (int k = start; k < list.Count; k++) if (list[k] != null) list[k].SetActive(false);
        }

        private void HideSupports()
        {
            HideFrom(spars, 0);
            HideFrom(chocks, 0);
            HideFrom(posts, 0);
            HideFrom(beams, 0);
            foreach (var l in lashings) if (l != null) l.gameObject.SetActive(false);
            foreach (var c in walkParts) if (c != null) c.enabled = false;
        }

        private void UpdateWalkBox(Bounds b, Quaternion F)
        {
            if (walk == null) return;
            if (walkBox == null)
            {
                var go = new GameObject("dinghy_on_deck");
                go.transform.SetParent(walk, false);
                var any = walk.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.name.StartsWith("dinghy_"));
                if (any != null) go.layer = any.gameObject.layer;
                walkBox = go.AddComponent<BoxCollider>();
            }
            walkBox.transform.localPosition = Vector3.zero;
            walkBox.transform.localRotation = F;
            walkBox.center = b.center;
            walkBox.size = b.size;
            walkBox.enabled = true;
        }

        private void SetIgnore(bool ignore)
        {
            if (stowedCols == null || shipCols == null) return;
            foreach (var a in stowedCols)
                foreach (var c in shipCols)
                    if (a != null && c != null) Physics.IgnoreCollision(a, c, ignore);
        }
    }
}