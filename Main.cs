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
                // 蓝图构建必须远离 UI 构建路径：放在常驻协程里后台完成。
                // 在 GetDefaultBlueprint 里现做会拖慢/打断读档时的法术书绑定。
                if (UIHost.Instance != null) UIHost.Instance.BeginWarmup();
            }
            catch (Exception e)
            {
                Log("UIHost create failed: " + e.Message);
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
        private static float s_LastRegisterCheck;

        /// <summary>蓝图是否已经构建完成（由后台协程轮询驱动）。</summary>
        internal static bool IsBuilt
        {
            get { return s_Built && s_Sets.Count > 0; }
        }

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
                // 蓝图还没加载完，后台协程会自动重试，这里静默返回（否则会刷屏）
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

                // 关键：这里绝不触发构建。该方法是游戏的运行时查询路径（读档、UI 绑定、
                // 施法计算都会调），一旦在里面做蓝图克隆，就会打断读档时法术书的初始化
                // —— 表现为"第一次读档法术书消失，第二次又正常"。
                if (!IsBuilt) return false;

                ExtraSet set;
                if (!s_Sets.TryGetValue(comp, out set)) return false;

                List<BlueprintAbility> list = touch ? set.Touch : set.Default;
                int i = index - Main.BaseSlots;
                if (i < 0 || i >= list.Count) return false;
                if (list[i] == null) return false;

                MaybeReRegister();
                result = list[i];
                return true;
            }
            catch (Exception e)
            {
                Main.Log("TryGetExtra error: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 保险：若蓝图缓存被清空（回主菜单、切场景等），把自建蓝图重新塞回去。
        /// 最多每 5 秒检查一次，避免在每帧路径上重复遍历。
        /// </summary>
        private static void MaybeReRegister()
        {
            if (!IsBuilt) return;
            float now = Time.realtimeSinceStartup;
            if (now - s_LastRegisterCheck < 5f) return;
            s_LastRegisterCheck = now;
            EnsureRegistered();
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
                if (s_SelectedField == null)
                {
                    Main.Log("SelectedSlotIndex backing field not found, fallback to vanilla");
                    return true;
                }

                int current = (int)s_SelectedField.GetValue(__instance);
                if (current == slotIndex) return false;

                int max = Main.TargetSlots - 1;

                // 保险：读档时游戏可能恢复上次选中的 11~20 槽，而此刻蓝图还没建好。
                // 这时把索引落回原版合法范围，避免 UI 拿到 null 蓝图后绑定失败
                // （这是"第一次读档法术书消失"的第二条可能路径）。
                if (slotIndex >= Main.BaseSlots && !ExtraBlueprints.IsBuilt)
                {
                    max = Main.BaseSlots - 1;
                }

                int clamped = Mathf.Clamp(slotIndex, 0, max);
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
        /// <summary>整块栅格相对"游戏原生位置"的整体平移（正 y = 向上）。一格 ≈ 53 × 58。</summary>
        public const float ShiftX = 0f;    // 0 = 左移一格（原为 +53）
        public const float ShiftY = 29f;   // 下移半格（原为 +58）

        // Ensure 时记录的第一排原生基准（不含平移）。同步协程靠它把两排压在目标位置上。
        private static float[] s_RowX;
        private static float s_RowY;
        private static bool s_HasRow;

        /// <summary>第二排是否已经建出来（协程重试用）。</summary>
        public static bool IsExtended(SpellbookMagicHackMixerView view)
        {
            try
            {
                if (view == null) return false;
                FieldInfo f = Main.FindField(typeof(SpellbookMagicHackMixerView), "m_Slots");
                if (f == null) return false;
                var group = f.GetValue(view) as OwlcatToggleGroup;
                if (group == null) return false;

                // 与 Ensure 相同的父节点判定逻辑
                Transform parent = group.transform;
                if (parent.Find("Slot_1") == null)
                {
                    OwlcatToggle t = FindToggleByName(group, "Slot_1");
                    if (t != null) parent = t.transform.parent;
                }
                if (parent == null) return false;

                return parent.Find("Slot_" + (Main.BaseSlots + 1)) != null;
            }
            catch
            {
                return false;
            }
        }

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

            // 整块栅格相对"游戏原生位置"的整体平移（正 y = 向上）
            const float shiftX = ShiftX;
            const float shiftY = ShiftY;

            // ---- 第一步：先让原生布局跑完，再读第一排的真实列坐标 ----
            // 这样列位置/列距完全跟随游戏（分辨率、UI 缩放变化都不会再错位）。
            var rowX = new float[Main.BaseSlots];
            bool haveRow = false;

            try
            {
                Canvas.ForceUpdateCanvases();
                var parentRt = parent as RectTransform;
                if (parentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
                haveRow = TryReadRowX(parent, rowX);
            }
            catch (Exception e)
            {
                Main.Log("Read native row failed: " + e.Message);
            }

            // 必须在强制布局之后再读 Slot_1 的位置（布局会改写它）
            Vector3 pos1 = rt1.localPosition;

            // 兜底：布局还没生效（10 个坐标全等）时用固定步进推算
            const float fallbackDx = 53f;
            if (!haveRow)
            {
                for (int i = 0; i < Main.BaseSlots; i++) rowX[i] = pos1.x + i * fallbackDx;
            }

            float baseY = pos1.y + shiftY;
            float rowStep = (rt1.rect.height > 10f) ? rt1.rect.height + 10f : 58f;

            // ---- 第二步：停掉自动布局 ----
            // 必须先 enabled=false 立即生效，再 Destroy；否则它会在帧末把刚设的坐标拉回去。
            DisableLayout<HorizontalLayoutGroup>(parent.gameObject);
            DisableLayout<VerticalLayoutGroup>(parent.gameObject);
            DisableLayout<GridLayoutGroup>(parent.gameObject);

            // 第一排：逐格套用原生 x + 整体平移，不重算间距（避免累积误差）
            for (int n = 1; n <= Main.BaseSlots; n++)
            {
                Transform t = parent.Find("Slot_" + n);
                var rt = t as RectTransform;
                if (rt == null) continue;
                rt.localPosition = new Vector3(rowX[n - 1] + shiftX, baseY, 0f);
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

                    // 落在第一排对应格子的正下方（x 直接复制第一排那一列）
                    var nrt = go.transform as RectTransform;
                    if (nrt != null)
                    {
                        int col = n - Main.BaseSlots - 1;
                        float x = (col >= 0 && col < rowX.Length) ? rowX[col] : rowX[0] + col * fallbackDx;
                        nrt.localPosition = new Vector3(x + shiftX, baseY - rowStep, 0f);
                    }

                    created++;
                }
                catch (Exception e)
                {
                    Main.Log("Create Slot_" + n + " failed: " + e);
                }
            }

            // 记录原生基准：同步协程之后每帧都靠它把两排压在"原生 + 平移"的位置上。
            // （实测游戏会在面板打开后再次改写第一排坐标，只写一次必然被冲掉。）
            s_RowX = (float[])rowX.Clone();
            s_RowY = pos1.y;
            s_HasRow = true;

            float stepX = (Main.BaseSlots > 1) ? (rowX[Main.BaseSlots - 1] - rowX[0]) / (Main.BaseSlots - 1) : 0f;
            Main.Log("UI extended: created " + created
                     + " slots, nativeRow=" + (haveRow ? "yes" : "fallback")
                     + " pos1=(" + pos1.x.ToString("F1") + "," + pos1.y.ToString("F1") + ")"
                     + " step=" + stepX.ToString("F1") + "/" + rowStep.ToString("F1")
                     + " cell=" + rt1.rect.width.ToString("F1") + "x" + rt1.rect.height.ToString("F1"));
        }

        /// <summary>
        /// 把第二排重新贴到第一排对应格子的正下方。
        ///
        /// 为什么需要反复调用：面板打开后的若干帧里，游戏自身还会再调一次第一排的位置
        /// （实测列距会被改小），"设一次就不管"必然错位。这里直接跟随第一排的实际
        /// localPosition，无论它怎么变，两排都严丝合缝。
        /// </summary>
        public static void SyncSecondRow(SpellbookMagicHackMixerView view)
        {
            if (view == null) return;

            FieldInfo f = Main.FindField(typeof(SpellbookMagicHackMixerView), "m_Slots");
            if (f == null) return;
            var group = f.GetValue(view) as OwlcatToggleGroup;
            if (group == null) return;

            Transform parent = group.transform;
            var rt1 = parent.Find("Slot_1") as RectTransform;
            if (rt1 == null)
            {
                OwlcatToggle t = FindToggleByName(group, "Slot_1");
                if (t == null) return;
                parent = t.transform.parent;
                rt1 = t.transform as RectTransform;
            }
            if (rt1 == null || parent == null) return;

            float rowStep = (rt1.rect.height > 10f) ? rt1.rect.height + 10f : 58f;

            bool pinned = s_HasRow && s_RowX != null && s_RowX.Length == Main.BaseSlots;

            for (int i = 0; i < Main.BaseSlots; i++)
            {
                var a = parent.Find("Slot_" + (i + 1)) as RectTransform;
                var b = parent.Find("Slot_" + (i + 1 + Main.BaseSlots)) as RectTransform;
                if (a == null && b == null) continue;

                float x, y;

                if (pinned)
                {
                    // 目标位置 = 原生基准 + 整体平移。两排都持续压在这里，
                    // 游戏若在别处改写第一排坐标，下一帧就会被纠回来。
                    x = s_RowX[i] + ShiftX;
                    y = s_RowY + ShiftY;
                }
                else
                {
                    // 没有基准（面板结构异常）时退回"跟随第一排"
                    if (a == null) continue;
                    Vector3 p = a.localPosition;
                    x = p.x;
                    y = p.y;
                }

                if (a != null) SetLocalPos(a, x, y);
                if (b != null) SetLocalPos(b, x, y - rowStep);
            }
        }

        /// <summary>位置没变化就不写，避免每帧把 Canvas 标脏。</summary>
        private static void SetLocalPos(RectTransform rt, float x, float y)
        {
            Vector3 cur = rt.localPosition;
            if (Mathf.Abs(cur.x - x) < 0.01f && Mathf.Abs(cur.y - y) < 0.01f) return;
            rt.localPosition = new Vector3(x, y, 0f);
        }

        /// <summary>
        /// 读取第一排 10 个槽位的真实 x 坐标。
        /// 布局还没生效时 10 个按钮返回同一个坐标，这时必须判定为失败（否则整块会塌成一个点）。
        /// </summary>
        private static bool TryReadRowX(Transform parent, float[] dst)
        {
            for (int i = 0; i < dst.Length; i++)
            {
                Transform t = parent.Find("Slot_" + (i + 1));
                var rt = t as RectTransform;
                if (rt == null) return false;
                dst[i] = rt.localPosition.x;
            }

            for (int i = 1; i < dst.Length; i++)
            {
                if (dst[i] - dst[i - 1] < 1f) return false;   // 未按列排开
            }

            return (dst[dst.Length - 1] - dst[0]) >= 50f;      // 总跨度太小说明没布局
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

        /// <summary>
        /// 后台把第 11~20 槽位的蓝图建好。蓝图要等游戏资源加载完才能拿到，
        /// 所以这里用协程轮询，构建好就退出。整个过程与 UI 无关，绝不阻塞读档。
        /// </summary>
        public void BeginWarmup()
        {
            try
            {
                StartCoroutine(WarmupBlueprints());
            }
            catch (Exception e)
            {
                Main.Log("Warmup coroutine start failed: " + e.Message);
            }
        }

        private IEnumerator WarmupBlueprints()
        {
            while (!ExtraBlueprints.IsBuilt)
            {
                try
                {
                    ExtraBlueprints.Ensure();
                }
                catch (Exception e)
                {
                    Main.Log("Warmup Ensure failed: " + e.Message);
                }

                if (ExtraBlueprints.IsBuilt) break;
                yield return new WaitForSecondsRealtime(1f);
            }

            Main.Log("Blueprint warmup finished");
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

            // 重试若干次：面板刚打开时布局可能还没算完，Ensure 会自行判断并退回公式法
            bool extended = false;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (view == null) yield break;

                try
                {
                    SlotsUI.Ensure(view);
                }
                catch (Exception e)
                {
                    Main.Log("Delayed slot UI ensure failed: " + e);
                }

                if (SlotsUI.IsExtended(view)) { extended = true; break; }
                yield return new WaitForSecondsRealtime(0.2f);
            }

            if (!extended)
            {
                Main.Log("Slot UI extension did not take effect after retries");
                yield break;
            }

            // 每帧同步：既跟随第一排，也在游戏改写坐标后立刻纠回来。
            // SetLocalPos 在位置没变时不写入，所以稳定后不会产生额外开销。
            while (view != null)
            {
                try { SlotsUI.SyncSecondRow(view); } catch { }

                if (!view.gameObject.activeInHierarchy)
                    yield return new WaitForSecondsRealtime(0.5f);   // 面板隐藏时降频
                else
                    yield return null;
            }

            Main.Log("Slot UI sync loop ended (panel destroyed)");
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

    // 注意：这里曾经有 Patch_OnSlotChange，在 OnSlotChange 的 Prefix 里直接重排槽位。
    // 实测会导致严重问题：读档时游戏会自动选中一个槽位 → 触发 OnSlotChange → 此时
    // 法术书 UI 仍在初始化，在事件里 Destroy 布局组件并重排，会打断后续绑定，
    // 表现为"第一次读档法术书消失，第二次读档又正常"。
    // 现在所有 UI 工作只在 UIHost 的协程里做（等面板可见 + 布局完成后），
    // 绝不介入游戏的运行时事件。
}
