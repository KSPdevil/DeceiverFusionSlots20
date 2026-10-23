using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MagicHack;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Newtonsoft.Json;
using Owlcat.Runtime.UI.Controls.Toggles;
using UnityEngine;
using UnityEngine.UI;

namespace DeceiverFusionSlots20
{
    public class Main
    {
        /// <summary>原版槽位数</summary>
        public const int BaseSlots = 10;

        /// <summary>目标槽位数</summary>
        public const int TargetSlots = 20;

        /// <summary>MagicDeceiverSpellbook 的蓝图 GUID</summary>
        public const string MagicDeceiverSpellbookGuid = "587066af76a74f47a904bb017697ba08";

        private static readonly object s_Lock = new object();

        public static void Load()
        {
            try
            {
                var harmony = new Harmony("com.custom.deceiverfusionslots20");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log("=== Mod loaded: MagicHack slots " + BaseSlots + " -> " + TargetSlots + " ===");
            }
            catch (Exception e)
            {
                Log("FATAL: Harmony PatchAll failed: " + e);
            }

            try
            {
                UIHost.Create();
            }
            catch (Exception e)
            {
                Log("UIHost create failed: " + e.Message);
            }

            try
            {
                ExtraBlueprints.Ensure();
            }
            catch (Exception e)
            {
                Log("Early blueprint setup failed (lazy retry enabled): " + e);
            }
        }

        public static object Lock
        {
            get { return s_Lock; }
        }

        private static bool s_LogChecked;

        /// <summary>
        /// 调试日志。标记 [Conditional] 后，正常编译时所有调用点会被编译器直接剔除，
        /// 运行时不产生任何文件与开销。需要排查问题时用 build.ps1 -Debug 编译即可打开。
        /// </summary>
        [System.Diagnostics.Conditional("DFS20_DEBUG")]
        public static void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeceiverFusionSlots20.log");
                if (!s_LogChecked)
                {
                    s_LogChecked = true;
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 4 * 1024 * 1024)
                    {
                        fi.Delete();
                    }
                }
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>沿继承链查找字段（DeclaredOnly，避免歧义）</summary>
        public static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }
    }

    // =========================================================================================
    //  1) 数据层：给 MagicHackSpellbookComponent 增加第 11~20 号槽位对应的蓝图对
    //     原版 m_Abilities / m_TouchAbilities 只有 10 个元素，index >= 10 一律返回 null
    // =========================================================================================
    internal static class ExtraBlueprints
    {
        internal class ExtraSet
        {
            public readonly List<BlueprintAbility> Default = new List<BlueprintAbility>();
            public readonly List<BlueprintAbility> Touch = new List<BlueprintAbility>();
        }

        private static readonly Dictionary<MagicHackSpellbookComponent, ExtraSet> s_Sets =
            new Dictionary<MagicHackSpellbookComponent, ExtraSet>();

        private static bool s_Built;
        private static bool s_Building;

        /// <summary>构建（幂等）。构造 10 组新蓝图 = 原版 0~9 号的克隆，供 11~20 号槽位使用。</summary>
        public static void Ensure()
        {
            if (s_Built || s_Building) return;
            lock (Main.Lock)
            {
                if (s_Built || s_Building) return;
                s_Building = true;
                try
                {
                    Build();
                }
                catch (Exception e)
                {
                    Main.Log("Build extra blueprints failed: " + e);
                }
                finally
                {
                    s_Building = false;
                }
            }
        }

        private static void Build()
        {
            BlueprintSpellbook spellbook = null;
            try
            {
                spellbook = ResourcesLibrary.TryGetBlueprint<BlueprintSpellbook>(Main.MagicDeceiverSpellbookGuid);
            }
            catch (Exception e)
            {
                Main.Log("TryGetBlueprint(MagicDeceiverSpellbook) failed: " + e.Message);
            }

            if (spellbook == null)
            {
                Main.Log("MagicDeceiverSpellbook not loaded yet, will retry lazily.");
                return;
            }

            MagicHackSpellbookComponent comp = null;
            foreach (var raw in spellbook.ComponentsArray)
            {
                comp = raw as MagicHackSpellbookComponent;
                if (comp != null) break;
            }
            if (comp == null)
            {
                Main.Log("MagicHackSpellbookComponent not found on MagicDeceiverSpellbook!");
                return;
            }

            FieldInfo defField = Main.FindField(typeof(MagicHackSpellbookComponent), "m_Abilities");
            FieldInfo touField = Main.FindField(typeof(MagicHackSpellbookComponent), "m_TouchAbilities");
            if (defField == null || touField == null)
            {
                Main.Log("m_Abilities / m_TouchAbilities field not found!");
                return;
            }

            var defs = defField.GetValue(comp) as BlueprintAbilityReference[];
            var tous = touField.GetValue(comp) as BlueprintAbilityReference[];
            if (defs == null || tous == null)
            {
                Main.Log("m_Abilities / m_TouchAbilities is null!");
                return;
            }

            Main.Log("Base slot blueprints: default=" + defs.Length + ", touch=" + tous.Length);

            var set = new ExtraSet();
            for (int i = 0; i < Main.BaseSlots; i++)
            {
                BlueprintAbility srcDefault = (i < defs.Length && defs[i] != null) ? defs[i].Get() : null;
                BlueprintAbility srcTouch = (i < tous.Length && tous[i] != null) ? tous[i].Get() : null;

                int slotNumber = i + Main.BaseSlots + 1; // 11..20
                set.Default.Add(Clone(srcDefault, "MagicHackDefaultSlot" + slotNumber));
                set.Touch.Add(Clone(srcTouch, "MagicHackTouchSlot" + slotNumber));
            }

            s_Sets[comp] = set;
            s_Built = true;
            Main.Log("Extra blueprint pairs created: " + set.Default.Count + " default / " + set.Touch.Count + " touch");
        }

        /// <summary>
        /// 供 Harmony Prefix 调用：index 在 11..20（0 基为 10..19）时返回自建蓝图。
        /// </summary>
        public static bool TryGetExtra(MagicHackSpellbookComponent comp, int index, bool touch, out BlueprintAbility result)
        {
            result = null;
            try
            {
                if (comp == null) return false;
                if (index < Main.BaseSlots || index >= Main.TargetSlots) return false;

                Ensure();
                EnsureRegistered();

                ExtraSet set;
                if (!s_Sets.TryGetValue(comp, out set)) return false;

                List<BlueprintAbility> list = touch ? set.Touch : set.Default;
                int i = index - Main.BaseSlots;
                if (i < 0 || i >= list.Count) return false;
                if (list[i] == null) return false;

                result = list[i];
                return true;
            }
            catch (Exception e)
            {
                Main.Log("TryGetExtra error: " + e.Message);
                return false;
            }
        }

        /// <summary>保险：若蓝图缓存被清空（回主菜单等），把自建蓝图重新塞回去，避免读档时找不到。</summary>
        private static void EnsureRegistered()
        {
            if (!s_Built) return;
            foreach (var kv in s_Sets)
            {
                foreach (BlueprintAbility bp in kv.Value.Default) ReRegister(bp);
                foreach (BlueprintAbility bp in kv.Value.Touch) ReRegister(bp);
            }
        }

        private static void ReRegister(BlueprintAbility bp)
        {
            if (bp == null) return;
            try
            {
                if (ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(bp.AssetGuid) != null) return;
                ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(bp.AssetGuid, bp);
                Main.Log("Re-registered blueprint " + bp.name);
            }
            catch { }
        }

        // ------------------------------------------------------------------ 克隆

        private static BlueprintAbility Clone(BlueprintAbility source, string newName)
        {
            if (source == null)
            {
                Main.Log("Clone source is null for " + newName);
                return null;
            }

            BlueprintGuid guid = DeterministicGuid("DeceiverFusionSlots20:" + newName);

            // 优先走游戏官方的 jbp(JSON) 反序列化路径，保证组件被完整深拷贝
            try
            {
                BlueprintAbility jsonClone = CloneViaJson(source, newName, guid);
                if (jsonClone != null) return jsonClone;
            }
            catch (Exception e)
            {
                Main.Log("JSON clone failed for " + source.name + ": " + e.Message);
            }

            // 兜底：浅拷贝（组件引用共享，但对象与 GUID 独立）
            try
            {
                return CloneShallow(source, newName, guid);
            }
            catch (Exception e)
            {
                Main.Log("Shallow clone failed for " + source.name + ": " + e);
                return null;
            }
        }

        private static BlueprintAbility CloneViaJson(BlueprintAbility source, string newName, BlueprintGuid guid)
        {
            var wrapper = new BlueprintJsonWrapper(source);
            wrapper.AssetId = guid.ToString();

            string text;
            using (var sw = new StringWriter())
            {
                Json.Serializer.Serialize(sw, wrapper);
                text = sw.ToString();
            }

            BlueprintJsonWrapper read;
            using (var sr = new StringReader(text))
            using (var jr = new JsonTextReader(sr))
            {
                read = Json.Serializer.Deserialize<BlueprintJsonWrapper>(jr);
            }

            var bp = read == null ? null : read.Data as BlueprintAbility;
            if (bp == null) return null;

            bp.name = newName;
            bp.AssetGuid = guid;
            bp.OnEnable();
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
            Main.Log("Cloned (json) " + source.name + " -> " + newName + " {" + guid + "}");
            return bp;
        }

        private static BlueprintAbility CloneShallow(BlueprintAbility source, string newName, BlueprintGuid guid)
        {
            MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            var bp = (BlueprintAbility)memberwise.Invoke(source, null);
            bp.name = newName;
            bp.AssetGuid = guid;
            bp.OnEnable();
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(guid, bp);
            Main.Log("Cloned (shallow) " + source.name + " -> " + newName + " {" + guid + "}");
            return bp;
        }

        /// <summary>稳定 GUID：同一槽位每次启动都得到同一个 GUID，保证存档兼容。</summary>
        private static BlueprintGuid DeterministicGuid(string seed)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                return new BlueprintGuid(new Guid(hash));
            }
        }
    }

    [HarmonyPatch(typeof(MagicHackSpellbookComponent))]
    internal static class Patch_MagicHackSpellbookComponent
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetDefaultBlueprint")]
        private static bool GetDefaultBlueprint_Prefix(MagicHackSpellbookComponent __instance, int index, ref BlueprintAbility __result)
        {
            BlueprintAbility found;
            if (ExtraBlueprints.TryGetExtra(__instance, index, false, out found))
            {
                __result = found;
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetTouchBlueprint")]
        private static bool GetTouchBlueprint_Prefix(MagicHackSpellbookComponent __instance, int index, ref BlueprintAbility __result)
        {
            BlueprintAbility found;
            if (ExtraBlueprints.TryGetExtra(__instance, index, true, out found))
            {
                __result = found;
                return false;
            }
            return true;
        }
    }

    // =========================================================================================
    //  2) VM 层：TrySelectSlot 里 Mathf.Clamp(slotIndex, 0, 9) 是"假槽位"的真正元凶
    //     它把 10~19 全部压成 9，并且 ResetCurrentInputToSlot() 读的还是第 10 槽的数据
    // =========================================================================================
    [HarmonyPatch(typeof(SpellbookMagicHackMixerVM), "TrySelectSlot")]
    internal static class Patch_TrySelectSlot
    {
        private static readonly FieldInfo s_SelectedField = Main.FindField(typeof(SpellbookMagicHackMixerVM), "<SelectedSlotIndex>k__BackingField");

        private static bool Prefix(SpellbookMagicHackMixerVM __instance, int slotIndex)
        {
            try
            {
                ExtraBlueprints.Ensure();

                if (s_SelectedField == null)
                {
                    Main.Log("SelectedSlotIndex backing field not found, fallback to vanilla");
                    return true;
                }

                int current = (int)s_SelectedField.GetValue(__instance);
                if (current == slotIndex) return false;

                int clamped = Mathf.Clamp(slotIndex, 0, Main.TargetSlots - 1);
                s_SelectedField.SetValue(__instance, clamped);

                if (clamped != current)
                    Main.Log("SelectSlot " + slotIndex + " -> " + clamped);

                __instance.ResetCurrentInputToSlot();
            }
            catch (Exception e)
            {
                Main.Log("TrySelectSlot prefix error: " + e);
            }
            return false;
        }
    }

    // =========================================================================================
    //  3) View 层：把 Slot_1 复制成 Slot_11 ~ Slot_20
    // =========================================================================================
    internal static class SlotsUI
    {
        public static void Ensure(SpellbookMagicHackMixerView view)
        {
            if (view == null) return;

            FieldInfo slotsField = Main.FindField(typeof(SpellbookMagicHackMixerView), "m_Slots");
            if (slotsField == null) { Main.Log("m_Slots field not found"); return; }

            var group = slotsField.GetValue(view) as OwlcatToggleGroup;
            if (group == null) { Main.Log("m_Slots is null"); return; }

            // 优先假定 toggle group 所在节点就是槽位按钮的父节点
            OwlcatToggle slot1 = null;
            Transform parent = group.transform;

            Transform slot1T = parent.Find("Slot_1");
            if (slot1T != null) slot1 = slot1T.GetComponent<OwlcatToggle>();

            // 兜底：从 OwlcatToggleGroup.m_Toggles 里找 Slot_1（和原版实现一致）
            if (slot1 == null)
            {
                slot1 = FindToggleByName(group, "Slot_1");
                if (slot1 != null) parent = slot1.transform.parent;
            }

            if (slot1 == null || parent == null)
            {
                Main.Log("Slot_1 not found, abort UI extension");
                return;
            }

            // 幂等：已经构建过就直接返回
            if (parent.Find("Slot_" + (Main.BaseSlots + 1)) != null) return;

            var rt1 = slot1.transform as RectTransform;
            if (rt1 == null) { Main.Log("Slot_1 has no RectTransform"); return; }

            // ---------------------------------------------------------------
            // 用公式推算栅格，不读每个按钮的实际坐标。
            // （踩过的坑：读 Slot_1..10 的 localPosition 再贴过去，布局还没跑完时
            //   10 个按钮坐标完全一样，整块会塌成一个点。这里只拿 Slot_1 当原点。）
            // ---------------------------------------------------------------
            Vector3 pos1 = rt1.localPosition;

            const float dx = 53f;        // 横向步进（一格宽）
            const float rowStep = 58f;   // 纵向步进（一格高）

            // 整块栅格相对 Slot_1 原位的偏移（正 y = 向上）
            const float offsetX = 53f;   // 右移一格
            const float offsetY = 58f;   // 上移一格（= 在上一版基础上再上移半格）

            float baseX = pos1.x + offsetX;
            float baseY = pos1.y + offsetY;

            // 关掉并移除自动布局，否则克隆体也会被排进第一排
            DisableLayout<HorizontalLayoutGroup>(parent.gameObject);
            DisableLayout<VerticalLayoutGroup>(parent.gameObject);
            DisableLayout<GridLayoutGroup>(parent.gameObject);

            // 第一排（原版 10 格）按同一套栅格重排，保证两排严格对齐
            for (int n = 1; n <= Main.BaseSlots; n++)
            {
                Transform t = parent.Find("Slot_" + n);
                var rt = t as RectTransform;
                if (rt == null) continue;
                rt.localPosition = new Vector3(baseX + (n - 1) * dx, baseY, 0f);
            }

            Type toggleType = slot1.GetType();
            int created = 0;

            for (int n = Main.BaseSlots + 1; n <= Main.TargetSlots; n++)
            {
                try
                {
                    GameObject go = UnityEngine.Object.Instantiate(slot1.gameObject, parent);
                    go.name = "Slot_" + n;

                    var toggle = go.GetComponent(toggleType) as OwlcatToggle;
                    if (toggle == null)
                    {
                        Main.Log("Clone for Slot_" + n + " has no OwlcatToggle");
                        continue;
                    }

                    // 克隆会带上 Slot_1 的 m_PrimaryIsOn，必须清掉，否则新按钮会自动点亮并抢走选中态
                    FieldInfo primary = Main.FindField(toggleType, "m_PrimaryIsOn");
                    if (primary != null) primary.SetValue(toggle, false);

                    group.RegisterToggle(toggle);
                    toggle.Set(false);

                    // 克隆体沿用了 Slot_1 的编号文本，改成 11..20
                    RelabelSlot(go, n);

                    // 定位到第一排对应格子的正下方
                    var nrt = go.transform as RectTransform;
                    if (nrt != null)
                    {
                        float col = n - Main.BaseSlots - 1;
                        nrt.localPosition = new Vector3(baseX + col * dx, baseY - rowStep, 0f);
                    }

                    created++;
                }
                catch (Exception e)
                {
                    Main.Log("Create Slot_" + n + " failed: " + e);
                }
            }

            Main.Log("UI extended: created " + created
                     + " slots, pos1=(" + pos1.x.ToString("F1") + "," + pos1.y.ToString("F1") + ")"
                     + " base=(" + baseX.ToString("F1") + "," + baseY.ToString("F1") + ")"
                     + " cell=" + rt1.rect.width.ToString("F1") + "x" + rt1.rect.height.ToString("F1"));
        }

        private static void DisableLayout<T>(GameObject go) where T : Behaviour
        {
            var comp = go.GetComponent<T>();
            if (comp == null) return;
            comp.enabled = false;
            UnityEngine.Object.Destroy(comp);
        }

        /// <summary>
        /// 把克隆槽位里的编号文本从 "1" 改成它自己的序号（11..20）。
        /// 用反射改，避免依赖 TextMeshPro 的编译期引用；只动纯数字且值在 1..10 的文本。
        /// </summary>
        private static void RelabelSlot(GameObject go, int number)
        {
            try
            {
                string target = number.ToString();
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;

                    Type t = c.GetType();
                    string tn = t.Name;
                    if (tn != "TextMeshProUGUI" && tn != "TextMeshPro" && tn != "Text") continue;

                    PropertyInfo p = t.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    if (p == null) continue;

                    string cur = p.GetValue(c, null) as string;
                    if (string.IsNullOrEmpty(cur)) continue;

                    int parsed;
                    if (!int.TryParse(cur.Trim(), out parsed)) continue;
                    if (parsed < 1 || parsed > Main.BaseSlots) continue;

                    p.SetValue(c, target, null);
                }
            }
            catch (Exception e)
            {
                Main.Log("RelabelSlot(" + number + ") failed: " + e.Message);
            }
        }

        private static OwlcatToggle FindToggleByName(OwlcatToggleGroup group, string name)
        {
            try
            {
                FieldInfo f = Main.FindField(typeof(OwlcatToggleGroup), "m_Toggles");
                if (f == null) return null;

                var dict = f.GetValue(group) as IDictionary;
                if (dict == null) return null;

                foreach (DictionaryEntry entry in dict)
                {
                    var comp = entry.Key as Component;
                    if (comp != null && comp.gameObject.name == name) return comp as OwlcatToggle;
                }
            }
            catch (Exception e)
            {
                Main.Log("FindToggleByName failed: " + e.Message);
            }
            return null;
        }
    }

    /// <summary>
    /// 独立的常驻协程宿主：等面板可见、布局算完后再补槽位按钮，
    /// 这样打开法术书就能直接看到 20 个槽，不必先点一下。
    /// </summary>
    public class UIHost : MonoBehaviour
    {
        public static UIHost Instance;

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("DeceiverFusionSlots20_UIHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<UIHost>();
        }

        public void Schedule(SpellbookMagicHackMixerView view)
        {
            if (view == null) return;
            try
            {
                StartCoroutine(Run(view));
            }
            catch (Exception e)
            {
                Main.Log("Schedule coroutine failed: " + e.Message);
            }
        }

        private IEnumerator Run(SpellbookMagicHackMixerView view)
        {
            int guard = 0;
            while (guard++ < 600)
            {
                if (view == null) yield break;
                if (view.gameObject.activeInHierarchy) break;
                yield return null;
            }

            // 等两帧，确保 LayoutGroup 已经完成一次布局
            yield return new WaitForEndOfFrame();
            yield return null;

            if (view == null) yield break;

            try
            {
                SlotsUI.Ensure(view);
            }
            catch (Exception e)
            {
                Main.Log("Delayed slot UI ensure failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(SpellbookMagicHackMixerView), "BindViewImplementation")]
    internal static class Patch_BindViewImplementation
    {
        private static void Postfix(SpellbookMagicHackMixerView __instance)
        {
            try
            {
                if (UIHost.Instance != null) UIHost.Instance.Schedule(__instance);
            }
            catch (Exception e)
            {
                Main.Log("BindView postfix error: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SpellbookMagicHackMixerView), "OnSlotChange")]
    internal static class Patch_OnSlotChange
    {
        private static void Prefix(SpellbookMagicHackMixerView __instance, OwlcatToggle toggle)
        {
            try
            {
                SlotsUI.Ensure(__instance);
            }
            catch (Exception e)
            {
                Main.Log("OnSlotChange prefix error: " + e);
            }
        }
    }
}
