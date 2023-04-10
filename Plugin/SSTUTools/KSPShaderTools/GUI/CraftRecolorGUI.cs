﻿using KSPShaderTools.Settings;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace KSPShaderTools
{
    public class CraftRecolorGUI : MonoBehaviour
    {
        private static int graphWidth = 400;
        private static int graphHeight = 540;
        private static int sectionHeight = 100;
        private static float windowX = -1;
        private static float windowY = -1;
        private static int id;
        private static Rect windowRect = new Rect(Screen.width - 500, 40, graphWidth, graphHeight);
        private static Vector2 scrollPos;
        private static Vector2 presetColorScrollPos;
        private static GUIStyle nonWrappingLabelStyle = null;

        private List<ModuleRecolorData> moduleRecolorData = new List<ModuleRecolorData>();
        
        internal Action guiCloseAction;

        private SectionRecolorData sectionData;
        /// <summary>
        /// Defines which ModuleRecolorData is used for UI setup and callbacks
        /// </summary>
        private int moduleIndex = -1;
        /// <summary>
        /// Defines which subsection of the selected module is currently being edited
        /// </summary>
        private int sectionIndex = -1;
        /// <summary>
        /// Defines which column the user is editing - main/second/detail
        /// </summary>
        private int colorIndex = -1;
        private string rStr, gStr, bStr, aStr, mStr, dStr;//string caches of color values//TODO -- set initial state when a section color is selected
        private static RecoloringData editingColor;
        private static RecoloringData[] storedPattern;
        private static RecoloringData storedColor;
        /// <summary>
        /// The name of the currently selected preset color group
        /// </summary>
        private static string groupName = "FULL";
        /// <summary>
        /// Index into the list of groups for the currently selected group
        /// </summary>
        private static int groupIndex = 0;

        private static bool scrollLock = false;

        public static Part openPart;

        public void Awake()
        {
            id = GetInstanceID();
            graphWidth = TUGameSettings.RecolorGUIWidth;// TexturesUnlimitedLoader.recolorGUIWidth;
            graphHeight = TUGameSettings.RecolorGUIHeight;// TexturesUnlimitedLoader.recolorGUITotalHeight;
            sectionHeight = TUGameSettings.RecolorGUITopHeight;// TexturesUnlimitedLoader.recolorGUISectionHeight;
            if (windowX == -1)
            {
                windowRect.x = Screen.width - (graphWidth + 100);
            }
            else
            {
                windowRect.x = windowX;
                windowRect.y = windowY;
            }            
        }

        internal void openGUIPart(Part part)
        {
            windowRect.width = graphWidth;
            windowRect.height = graphHeight;
            if (part != openPart)
            {
                moduleIndex = -1;
                sectionIndex = -1;
                colorIndex = -1;
            }
            ControlTypes controls = ControlTypes.ALLBUTCAMERAS;
            controls = controls & ~ControlTypes.TWEAKABLES;
            InputLockManager.SetControlLock(controls, "SSTURecolorGUILock");
            setupForPart(part);
            if (moduleIndex < 0 || sectionIndex < 0)
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
                colorIndex = 0;
            }
            if (colorIndex < 0)
            {
                colorIndex = 0;
            }
            setupSectionData(moduleRecolorData[moduleIndex].sectionData[sectionIndex], colorIndex);
            openPart = part;
        }

        /// <summary>
        /// To be called from the external 'GuiCloseAction' delegate.
        /// </summary>
        internal void closeGui()
        {
            closeSectionGUI();
            moduleRecolorData.Clear();
            sectionData = null;
            openPart = null;
            InputLockManager.RemoveControlLock("SSTURecolorGUILock");
            InputLockManager.RemoveControlLock("SSTURecolorGUILock2");
            colorIndex = -1;
            moduleIndex = -1;
            sectionIndex = -1;
        }

        internal void refreshGui(Part part)
        {
            if (part != openPart) { return; }

            moduleRecolorData.Clear();
            setupForPart(part);

            int len = moduleRecolorData.Count;
            if (moduleIndex >= len)
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
            }
            len = moduleRecolorData[moduleIndex].sectionData.Length;
            if (sectionIndex >= len)
            {
                findFirstRecolorable(moduleIndex, out moduleIndex, out sectionIndex);
            }

            ModuleRecolorData mrd = moduleRecolorData[moduleIndex];
            SectionRecolorData srd = mrd.sectionData[sectionIndex];
            if (!srd.recoloringSupported())
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
            }

            setupSectionData(moduleRecolorData[moduleIndex].sectionData[sectionIndex], colorIndex);
        }

        private void setupForPart(Part part)
        {
            List<IRecolorable> mods = part.FindModulesImplementing<IRecolorable>();
            foreach (IRecolorable mod in mods)
            {
                ModuleRecolorData data = new ModuleRecolorData((PartModule)mod, mod);
                moduleRecolorData.Add(data);
            }
        }

        private void findFirstRecolorable(out int module, out int section)
        {
            int len = moduleRecolorData.Count;
            ModuleRecolorData mrd;
            for (int i = 0; i < len; i++)
            {
                mrd = moduleRecolorData[i];
                int len2 = mrd.sectionData.Length;
                SectionRecolorData srd;
                for (int k = 0; k < len2; k++)
                {
                    srd = mrd.sectionData[k];
                    if (srd.recoloringSupported())
                    {
                        module = i;
                        section = k;
                        return;
                    }
                }
            }
            Log.error("ERROR: Could not locate recolorable section for part: " + openPart);
            module = 0;
            section = 0;
        }

        private void findFirstRecolorable(int moduleStart, out int module, out int section)
        {
            module = moduleStart;
            if (moduleStart < moduleRecolorData.Count)
            {
                ModuleRecolorData mrd = moduleRecolorData[moduleStart];
                int len = mrd.sectionData.Length;
                SectionRecolorData srd;
                for (int i = 0; i < len; i++)
                {
                    srd = mrd.sectionData[i];
                    if (srd.recoloringSupported())
                    {
                        //found section in current module that supports recoloring, return it
                        section = i;
                        return;
                    }
                }
            }
            //if recolorable could not be found in current module selection, default to searching entire part
            findFirstRecolorable(out module, out section);
        }

        public void OnGUI()
        {
            //apparently trying to initialize this during OnAwake/etc fails, as unity is dumb and requires that it be done during an OnGUI call
            //serious -- you cant even access the GUI.skin except in OnGUi...
            if (nonWrappingLabelStyle == null)
            {
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.wordWrap = false;
                nonWrappingLabelStyle = style;
            }
            windowRect = GUI.Window(id, windowRect, drawWindow, "部件重新着色"); // Part Recoloring
            windowX = windowRect.x;
            windowY = windowRect.y;
        }

        private void drawWindow(int id)
        {
            bool lockedScroll = false;
            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                lockedScroll = true;
                scrollLock = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "SSTURecolorGUILock2");
            }
            UnityEngine.GUILayout.BeginVertical();
            drawSectionSelectionArea();
            drawSectionRecoloringArea();
            drawPresetColorArea();
            if (GUILayout.Button("关闭")) // Close
            {
                guiCloseAction();//call the method in SSTULauncher to close this GUI
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
            if (!lockedScroll && scrollLock)
            {
                InputLockManager.RemoveControlLock("SSTURecolorGUILock2");
            }
        }

        private void setupSectionData(SectionRecolorData section, int colorIndex)
        {
            this.sectionData = section;
            this.colorIndex = colorIndex;
            if (section.colors == null) { return; }
            editingColor = sectionData.colors[colorIndex];
            rStr = (editingColor.color.r * 255f).ToString("F0");
            gStr = (editingColor.color.g * 255f).ToString("F0");
            bStr = (editingColor.color.b * 255f).ToString("F0");
            aStr = (editingColor.specular * 255f).ToString("F0");
            mStr = (editingColor.metallic * 255f).ToString("F0");
            dStr = (editingColor.detail * 100).ToString("F0");
        }

        private void closeSectionGUI()
        {
            sectionData = null;
            editingColor = new RecoloringData(Color.white, 0, 0, 1);
            rStr = gStr = bStr = aStr = mStr = dStr = "255";
            colorIndex = 0;
        }

        private void drawSectionSelectionArea()
        {
            GUILayout.BeginHorizontal();
            Color old = GUI.color;
            float buttonWidth = 70;
            float scrollWidth = 40;
            float sectionTitleWidth = graphWidth - scrollWidth - buttonWidth * 3 - scrollWidth;
            GUILayout.Label("分段", GUILayout.Width(sectionTitleWidth)); // Section
            GUI.color = colorIndex == 0 ? Color.red : old;
            GUILayout.Label("主要", GUILayout.Width(buttonWidth)); // Main
            GUI.color = colorIndex == 1 ? Color.red : old;
            GUILayout.Label("次要", GUILayout.Width(buttonWidth)); // Second
            GUI.color = colorIndex == 2 ? Color.red : old;
            GUILayout.Label("局部", GUILayout.Width(buttonWidth)); // Detail
            GUI.color = old;
            GUILayout.EndHorizontal();
            Color guiColor = old;
            scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.Height(sectionHeight));
            int len = moduleRecolorData.Count;
            for (int i = 0; i < len; i++)
            {
                int len2 = moduleRecolorData[i].sectionData.Length;
                for (int k = 0; k < len2; k++)
                {
                    if (!moduleRecolorData[i].sectionData[k].recoloringSupported())
                    {
                        continue;
                    }
                    GUILayout.BeginHorizontal();
                    if ( k == sectionIndex && i == moduleIndex )
                    {
                        GUI.color = Color.red;
                    }
                    GUILayout.Label(moduleRecolorData[i].sectionData[k].sectionName, GUILayout.Width(sectionTitleWidth));
                    for (int m = 0; m < 3; m++)
                    {
                        int mask = 1 << m;
                        if (moduleRecolorData[i].sectionData[k].channelSupported(mask))
                        {
                            guiColor = moduleRecolorData[i].sectionData[k].colors[m].color;
                            guiColor.a = 1;
                            GUI.color = guiColor;
                            if (GUILayout.Button("重新着色", GUILayout.Width(70))) // Recolor
                            {
                                moduleIndex = i;
                                sectionIndex = k;
                                colorIndex = m;
                                setupSectionData(moduleRecolorData[i].sectionData[k], m);
                            }
                        }
                        else
                        {
                            GUILayout.Label("", GUILayout.Width(70));
                        }
                    }
                    GUI.color = old;
                    GUILayout.EndHorizontal();
                }
            }
            GUI.color = old;
            GUILayout.EndScrollView();
        }

        private void drawSectionRecoloringArea()
        {            
            if (sectionData == null)
            {
                return;
            }
            bool updated = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("编辑: ", GUILayout.Width(60)); // Editing
            GUILayout.Label(sectionData.sectionName);
            GUILayout.Label(getSectionLabel(colorIndex) + "颜色"); // Color
            GUILayout.FlexibleSpace();//to force everything to the left instead of randomly spaced out, while still allowing dynamic length adjustments
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (drawColorInputLine("红色", ref editingColor.color.r, ref rStr, sectionData.colorSupported(), 255, 1)) { updated = true; } // Red
            if (GUILayout.Button("载入样板", GUILayout.Width(120))) // Load Pattern
            {
                sectionData.colors[0] = storedPattern[0];
                sectionData.colors[1] = storedPattern[1];
                sectionData.colors[2] = storedPattern[2];
                editingColor = sectionData.colors[colorIndex];
                updated = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (drawColorInputLine("绿色", ref editingColor.color.g, ref gStr, sectionData.colorSupported(), 255, 1)) { updated = true; } // Green
            if (GUILayout.Button("保存样板", GUILayout.Width(120))) // Store Pattern
            {
                storedPattern = new RecoloringData[3];
                storedPattern[0] = sectionData.colors[0];
                storedPattern[1] = sectionData.colors[1];
                storedPattern[2] = sectionData.colors[2];
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (drawColorInputLine("蓝色", ref editingColor.color.b, ref bStr, sectionData.colorSupported(), 255, 1)) { updated = true; } // Blue
            if (GUILayout.Button("载入颜色", GUILayout.Width(120))) // Load Color
            {
                editingColor = storedColor;
                updated = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (drawColorInputLine("镜面反射", ref editingColor.specular, ref aStr, sectionData.specularSupported(), 255, 1)) { updated = true; } // Specular
            if (GUILayout.Button("保存颜色", GUILayout.Width(120))) // Store Color
            {
                storedColor = editingColor;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (sectionData.metallicSupported())
            {
                if (drawColorInputLine("金属", ref editingColor.metallic, ref mStr, true, 255, 1)) { updated = true; } // Metallic
            }
            else if (sectionData.hardnessSupported())
            {
                if (drawColorInputLine("硬物", ref editingColor.metallic, ref mStr, true, 255, 1)) { updated = true; } // Hardness
            }
            else
            {
                if (drawColorInputLine("金属", ref editingColor.metallic, ref mStr, false, 255, 1)) { updated = true; } // Metallic
            }
            if (GUILayout.Button("<", GUILayout.Width(20)))
            {
                groupIndex--;
                List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                if (groupIndex < 0) { groupIndex = gs.Count-1; }
                groupName = gs[groupIndex].name;
            }
            GUILayout.Label("色泽", GUILayout.Width(70)); // Palette
            if (GUILayout.Button(">", GUILayout.Width(20)))
            {
                groupIndex++;
                List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                if (groupIndex >= gs.Count) { groupIndex = 0; }
                groupName = gs[groupIndex].name;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (drawColorInputLine("细分 %", ref editingColor.detail, ref dStr, true, 100, 5)) { updated = true; } // Detail
            GUILayout.Label(groupName, GUILayout.Width(120));
            GUILayout.EndHorizontal();

            if (updated)
            {
                sectionData.colors[colorIndex] = editingColor;
                sectionData.updateColors();
            }
        }

        private void drawPresetColorArea()
        {
            if (sectionData == null)
            {
                return;
            }
            GUILayout.Label("选择预设颜色: "); // Select a preset color
            presetColorScrollPos = GUILayout.BeginScrollView(presetColorScrollPos, false, true);
            bool update = false;
            Color old = GUI.color;
            Color guiColor = old;
            List<RecoloringDataPreset> presetColors = PresetColor.getColorList(groupName);
            int len = presetColors.Count;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < len; i++)
            {
                if (i > 0 && i % 2 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                GUILayout.Label(presetColors[i].title, nonWrappingLabelStyle, GUILayout.Width(115));
                guiColor = presetColors[i].color;
                guiColor.a = 1f;
                GUI.color = guiColor;
                if (GUILayout.Button("选择", GUILayout.Width(55))) // Select
                {
                    editingColor = presetColors[i].getRecoloringData();
                    rStr = (editingColor.color.r * 255f).ToString("F0");
                    gStr = (editingColor.color.g * 255f).ToString("F0");
                    bStr = (editingColor.color.b * 255f).ToString("F0");
                    aStr = (editingColor.specular * 255f).ToString("F0");
                    mStr = (editingColor.metallic * 255f).ToString("F0");
                    //dStr = (editingColor.detail * 100f).ToString("F0");//leave detail mult as pre-specified value (user/config); it does not pull from preset colors at all
                    update = true;
                }
                GUI.color = old;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.color = old;
            if (sectionData.colors != null)
            {
                sectionData.colors[colorIndex] = editingColor;
                if (update)
                {
                    sectionData.updateColors();
                }
            }
        }

        private bool drawColorInputLine(string label, ref float val, ref string sVal, bool enabled, float mult, float max)
        {
            if (!enabled)
            {
                GUILayout.Label("", GUILayout.Width(60 + 120 + 60));
                return false;
            }
            //TODO -- text input validation for numbers only -- http://answers.unity3d.com/questions/18736/restrict-characters-in-guitextfield.html
            // also -- https://forum.unity3d.com/threads/text-field-for-numbers-only.106418/
            GUILayout.Label(label, GUILayout.Width(60));
            bool updated = false;
            float result = val;
            result = GUILayout.HorizontalSlider(val, 0, max, GUILayout.Width(120));
            if (result != val)
            {
                val = result;
                sVal = (val * mult).ToString("F0");
                updated = true;
            }
            string textOutput = GUILayout.TextField(sVal, 3, GUILayout.Width(60));
            if (sVal != textOutput)
            {
                sVal = textOutput;
                int iVal;
                if (int.TryParse(textOutput, out iVal))
                {
                    val = iVal / mult;
                    updated = true;
                }
            }
            return updated;
        }

        private string getSectionLabel(int index)
        {
            switch (index)
            {
                case 0:
                    return "主要"; // Main
                case 1:
                    return "次要"; // Secondary
                case 2:
                    return "局部"; // Detail
                default:
                    return "Unknown"; // 
            }
        }

    }

    public class ModuleRecolorData
    {
        public PartModule module;//must implement IRecolorable
        public IRecolorable iModule;//interface version of module
        public SectionRecolorData[] sectionData;

        public ModuleRecolorData(PartModule module, IRecolorable iModule)
        {
            this.module = module;
            this.iModule = iModule;
            string[] names = iModule.getSectionNames();
            int len = names.Length;
            sectionData = new SectionRecolorData[len];
            for (int i = 0; i < len; i++)
            {
                sectionData[i] = new SectionRecolorData(iModule, names[i], iModule.getSectionColors(names[i]), iModule.getSectionTexture(names[i]));
            }
        }
    }

    public class SectionRecolorData
    {
        public readonly IRecolorable owner;
        public readonly string sectionName;
        public RecoloringData[] colors;
        private TextureSet sectionTexture;

        public SectionRecolorData(IRecolorable owner, string name, RecoloringData[] colors, TextureSet set)
        {
            this.owner = owner;
            this.sectionName = name;
            this.colors = colors;
            this.sectionTexture = set;
            if (colors == null)
            {
                //owners may return null for set and/or colors if recoloring is unsupported
                set = sectionTexture = null;
            }
            //MonoBehaviour.print("Created section recolor data with texture set: " + set+" for section: "+name);
            if (set != null)
            {
                //MonoBehaviour.print("Set name: " + set.name + " :: " + set.title + " recolorable: " + set.supportsRecoloring);
            }
            else
            {
                Log.error("Set was null while setting up recoloring section for: "+name);
            }
        }

        public void updateColors()
        {
            owner.setSectionColors(sectionName, colors);
        }

        public bool recoloringSupported()
        {
            if (sectionTexture == null) { return false; }
            return sectionTexture.supportsRecoloring;
        }

        public bool colorSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 1) != 0;
        }

        public bool channelSupported(int mask)
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.recolorableChannelMask & mask) != 0;
        }

        public bool specularSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 2) != 0;
        }

        public bool metallicSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 4) != 0;
        }

        public bool hardnessSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 8) != 0;
        }

    }

}
