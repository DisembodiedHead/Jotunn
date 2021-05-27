﻿using Jotunn.Entities;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Jotunn.Managers
{
    /// <summary>
    ///     Manager for handling custom pieces added to the game.
    /// </summary>
    public class PieceManager : IManager
    {
        private static PieceManager _instance;
        /// <summary>
        ///     The singleton instance of this manager.
        /// </summary>
        public static PieceManager Instance
        {
            get
            {
                if (_instance == null) _instance = new PieceManager();
                return _instance;
            }
        }

        /// <summary>
        ///     Event that gets fired after all pieces were added to their respective PieceTables.
        ///     Your code will execute every time a new ObjectDB is created (on every game start).
        ///     If you want to execute just once you will need to unregister from the event after execution.
        /// </summary>
        public static event Action OnPiecesRegistered;

        internal GameObject PieceTableContainer;
        internal List<CustomPiece> Pieces = new();

        internal readonly Dictionary<string, PieceTable> PieceTables = new();
        internal readonly Dictionary<string, string> PieceTableNameMap = new();

        internal readonly Dictionary<string, Piece.PieceCategory> PieceCategories = new();

        private Piece.PieceCategory PieceCategoryMax = Piece.PieceCategory.Max;

        /// <summary>
        ///     Creates the piece table container and registers all hooks.
        /// </summary>
        public void Init()
        {
            // Create PieceTable Container
            PieceTableContainer = new GameObject("PieceTables");
            PieceTableContainer.transform.parent = Main.RootObject.transform;

            // Setup Hooks
            On.ObjectDB.Awake += RegisterCustomData;
            On.Player.Load += ReloadKnownRecipes;

            // Fire events as a late action in the detour so all mods can load before
            // Leave space for mods to forcefully run after us. 1000 is an arbitrary "good amount" of space.
            using (new DetourContext(int.MaxValue - 1000))
            {
                On.ObjectDB.Awake += InvokeOnPiecesRegistered;
            }
        }

        /// <summary>
        ///     Add a new <see cref="PieceTable"/> from <see cref="GameObject"/>.
        /// </summary>
        /// <param name="prefab">The prefab of the <see cref="PieceTable"/></param>
        public void AddPieceTable(GameObject prefab)
        {
            if (PieceTables.ContainsKey(prefab.name))
            {
                Logger.LogWarning($"Piece table {prefab.name} already added");
                return;
            }

            var table = prefab.GetComponent<PieceTable>();

            if (table == null)
            {
                Logger.LogError($"Prefab {prefab.name} has no PieceTable attached");
                return;
            }

            prefab.transform.parent = PieceTableContainer.transform;

            PieceTables.Add(prefab.name, table);

            //TODO: get the name of the item which has this table attached and add it to the name map
        }

        /// <summary>
        ///     Add a new <see cref="PieceTable"/> from string.<br />
        ///     Creates a <see cref="GameObject"/> with a <see cref="PieceTable"/> component and adds it to the manager.
        /// </summary>
        /// <param name="name">Name of the new piece table.</param>
        public void AddPieceTable(string name)
        {
            if (PieceTables.ContainsKey(name))
            {
                Logger.LogWarning($"Piece table {name} already added");
                return;
            }

            GameObject obj = new GameObject(name);
            obj.transform.parent = PieceTableContainer.transform;

            PieceTable table = obj.AddComponent<PieceTable>();
            PieceTables.Add(name, table);
        }

        /// <summary>
        ///     Get a <see cref="global::PieceTable"/> by name.<br /><br />
        ///     Search hierarchy:<br />
        ///     <list type="number">
        ///         <item>PieceTable with the exact name (e.g. "_HammerPieceTable")</item>
        ///         <item>PieceTable via "item" name (e.g. "Hammer")</item>
        ///     </list>
        /// </summary>
        /// <param name="name">Prefab or item name of the PieceTable</param>
        /// <returns>PieceTable prefab</returns>
        public PieceTable GetPieceTable(string name)
        {
            if (PieceTables.ContainsKey(name))
            {
                return PieceTables[name];
            }

            if (PieceTableNameMap.ContainsKey(name))
            {
                return PieceTables[PieceTableNameMap[name]];
            }

            //return PrefabManager.Cache.GetPrefab<PieceTable>(name);
            return null;
        }

        /// <summary>
        ///     Add a new <see cref="global::Piece.PieceCategory"/> by name. A new category
        ///     gets assigned a random integer for internal use. If you pass a vanilla category
        ///     the actual integer value of the enum is returned. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns>int value of the vanilla or custom category</returns>
        public Piece.PieceCategory AddPieceCategory(string name)
        {
            if (Enum.IsDefined(typeof(Piece.PieceCategory), name))
            {
                return (Piece.PieceCategory)Enum.Parse(typeof(Piece.PieceCategory), name);
            }

            if (PieceCategories.ContainsKey(name))
            {
                return PieceCategories[name];
            }

            Piece.PieceCategory categoryID = PieceCategories.Count() + Piece.PieceCategory.Max;

            PieceCategories.Add(name, categoryID);

            PieceCategoryMax += 1;

            return categoryID;
        }

        /// <summary>
        ///     Add a <see cref="CustomPiece"/> to the game.<br />
        ///     Checks if the custom piece is valid and unique and adds it to the list of custom pieces.<br />
        ///     Custom pieces are added to their respective <see cref="PieceTable"/>s after <see cref="ObjectDB.Awake"/>.
        /// </summary>
        /// <param name="customPiece">The custom piece to add.</param>
        /// <returns>true if the custom piece was added to the manager.</returns>
        public bool AddPiece(CustomPiece customPiece)
        {
            if (!customPiece.IsValid())
            {
                Logger.LogWarning($"Custom piece {customPiece} is not valid");
                return false;
            }
            if (Pieces.Contains(customPiece))
            {
                Logger.LogWarning($"Custom piece {customPiece} already added");
                return false;
            }

            // Add to the right layer if necessary
            if (customPiece.PiecePrefab.layer == 0)
            {
                customPiece.PiecePrefab.layer = LayerMask.NameToLayer("piece");
            }

            // Add the prefab to the PrefabManager
            PrefabManager.Instance.AddPrefab(customPiece.PiecePrefab);

            // Add the custom piece to the PieceManager
            Pieces.Add(customPiece);

            return true;
        }

        /// <summary>
        ///     Get a custom piece by its name.
        /// </summary>
        /// <param name="pieceName">Name of the piece to search.</param>
        /// <returns></returns>
        public CustomPiece GetPiece(string pieceName)
        {
            return Pieces.FirstOrDefault(x => x.PiecePrefab.name.Equals(pieceName));
        }

        /// <summary>
        ///     Remove a custom piece by its name.
        /// </summary>
        /// <param name="pieceName">Name of the piece to remove.</param>
        public void RemovePiece(string pieceName)
        {
            var piece = GetPiece(pieceName);
            if (piece == null)
            {
                Logger.LogWarning($"Could not remove piece {pieceName}: Not found");
                return;
            }

            Pieces.Remove(piece);
        }

        /// <summary>
        ///     Loop all items in the game and get all PieceTables used
        /// </summary>
        private void LoadPieceTables()
        {
            foreach (var item in ObjectDB.instance.m_items)
            {
                var table = item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

                if (table != null)
                {
                    if (!PieceTables.ContainsKey(table.name))
                    {
                        PieceTables.Add(table.name, table);
                    }
                    if (!PieceTableNameMap.ContainsKey(item.name))
                    {
                        PieceTableNameMap.Add(item.name, table.name);
                    }
                }
            }
        }

        private void CreatePieceCategories()
        {
            if (PieceCategories.Count > 0)
            {
                Logger.LogInfo($"---- Adding custom piece categories ----");

                // All piece tables using categories
                foreach (var table in PieceTables.Values.Where(x => x.m_useCategories))
                {
                    // Add empty lists up to the custom categories index
                    if (table.m_availablePieces.Count < (int)PieceCategoryMax)
                    {
                        for (int i = table.m_availablePieces.Count; i < (int)PieceCategoryMax;  i++)
                        {
                            table.m_availablePieces.Add(new List<Piece>());
                        }
                    }

                    // Add an empty "category piece list" for each custom category
                    foreach (var category in PieceCategories)
                    {
                        table.m_availablePieces.Add(new List<Piece>());
                    }

                    // Resize selectedPiece array
                    Array.Resize(ref table.m_selectedPiece, table.m_availablePieces.Count);
                }

                SceneManager.sceneLoaded += CreateCategoryTabs;
            }
            //_GameMain/GUI/PixelFix/IngameGui(Clone)/HUD/hudroot/BuildHud/bar/SelectionWindow/Categories
        }

        private void CreateCategoryTabs(Scene scene, LoadSceneMode mode)
        {
            // Get the GUI elements
            GameObject root = Hud.instance.m_pieceCategoryRoot;

            List<string> newNames = new List<string>(Hud.instance.m_buildCategoryNames);
            List<GameObject> newTabs = new List<GameObject>(Hud.instance.m_pieceCategoryTabs);

            // Add tabs to the GUI for every custom category
            foreach (var category in PieceCategories)
            {
                GameObject newTab = UnityEngine.Object.Instantiate(root.transform.Find("Misc")?.gameObject, root.transform);
                newTab.name = category.Key;
                
                UIInputHandler component = newTab.GetComponent<UIInputHandler>();
                component.m_onLeftDown = (Action<UIInputHandler>)Delegate.Combine(component.m_onLeftDown, new Action<UIInputHandler>(Hud.instance.OnLeftClickCategory));
                
                newNames.Add(category.Key);
                newTabs.Add(newTab);
            }

            // Replace the HUD arrays
            Hud.instance.m_buildCategoryNames = newNames.ToList();
            Hud.instance.m_pieceCategoryTabs = newTabs.ToArray();

            // Reorder tabs
            float offset = 0f;
            foreach (RectTransform tf in root.transform)
            {
                tf.anchoredPosition = new Vector2(offset, 0f);
                offset += 120f;
            }
/*
            // Reorder Tabs
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.spacing = 20;
*/
            SceneManager.sceneLoaded -= CreateCategoryTabs;

            // Register hooks for categories
            On.PieceTable.SetCategory += PieceTable_SetCustomCategory;
            On.PieceTable.NextCategory += PieceTable_NextCategory;
            On.PieceTable.PrevCategory += PieceTable_PrevCategory;
        }

        private void PieceTable_SetCustomCategory(On.PieceTable.orig_SetCategory orig, PieceTable self, int index)
        {
            orig(self, index);

            if (self.m_useCategories)
            {
                if (PieceCategories.ContainsValue((Piece.PieceCategory)index))
                {
                    self.m_selectedCategory = (Piece.PieceCategory)index;
                }
            }
        }

        private void PieceTable_NextCategory(On.PieceTable.orig_NextCategory orig, PieceTable self)
        {
            if (self.m_useCategories)
            {
                self.m_selectedCategory++;

                if (self.m_selectedCategory == PieceCategoryMax)
                {
                    self.m_selectedCategory = 0;
                }
            }

            PieceCategoryScroll(self.m_selectedCategory);
        }

        private void PieceTable_PrevCategory(On.PieceTable.orig_PrevCategory orig, PieceTable self)
        {
            if (self.m_useCategories)
            {
                self.m_selectedCategory--;

                if (self.m_selectedCategory < Piece.PieceCategory.Misc)
                {
                    self.m_selectedCategory = PieceCategoryMax - 1;
                }
            }

            PieceCategoryScroll(self.m_selectedCategory);
        }

        private void PieceCategoryScroll(Piece.PieceCategory selectedCategory)
        {
            var tab = Hud.instance.m_pieceCategoryTabs[(int)selectedCategory];

            if ((tab.transform as RectTransform).anchoredPosition.x < 120)
            {
                foreach (GameObject go in Hud.instance.m_pieceCategoryTabs)
                {
                    (go.transform as RectTransform).anchoredPosition += new Vector2(120f, 0);
                }
            }
            if ((tab.transform as RectTransform).anchoredPosition.x > 480)
            {
                foreach (GameObject go in Hud.instance.m_pieceCategoryTabs)
                {
                    (go.transform as RectTransform).anchoredPosition -= new Vector2(120f, 0);
                }
            }
        }

        private void RegisterInPieceTables()
        {
            if (Pieces.Count > 0)
            {
                Logger.LogInfo($"---- Adding custom pieces to the PieceTables ----");

                foreach (var customPiece in Pieces)
                {
                    try
                    {
                        // Fix references if needed
                        if (customPiece.FixReference)
                        {
                            customPiece.PiecePrefab.FixReferences();
                            customPiece.FixReference = false;
                        }

                        // Assign vfx_ExtensionConnection for StationExtensions
                        var extension = customPiece.PiecePrefab.GetComponent<StationExtension>();
                        if (extension != null && !extension.m_connectionPrefab)
                        {
                            extension.m_connectionPrefab = PrefabManager.Cache.GetPrefab<GameObject>("vfx_ExtensionConnection");
                        }

                        // Assign the piece to the actual PieceTable if not already in there
                        var pieceTable = GetPieceTable(customPiece.PieceTable);
                        if (pieceTable == null)
                        {
                            throw new Exception($"Could not find piecetable {customPiece.PieceTable}");
                        }
                        if (pieceTable.m_pieces.Contains(customPiece.PiecePrefab))
                        {
                            Logger.LogInfo($"Already added piece {customPiece}");
                        }
                        else
                        {
                            pieceTable.m_pieces.Add(customPiece.PiecePrefab);
                            Logger.LogInfo($"Added piece {customPiece} | Token: {customPiece.Piece.TokenName()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error while adding piece {customPiece}: {ex}");
                    }

                }
            }
        }

        private void RegisterCustomData(On.ObjectDB.orig_Awake orig, ObjectDB self)
        {
            orig(self);

            if (SceneManager.GetActiveScene().name == "main")
            {
                var isValid = self.IsValid();

                if (isValid)
                {
                    LoadPieceTables();
                    CreatePieceCategories();
                    RegisterInPieceTables();
                }
            }
        }

        private void InvokeOnPiecesRegistered(On.ObjectDB.orig_Awake orig, ObjectDB self)
        {
            orig(self);

            if (SceneManager.GetActiveScene().name == "main" && self.IsValid())
            {
                OnPiecesRegistered?.SafeInvoke();
            }
        }

        private void ReloadKnownRecipes(On.Player.orig_Load orig, Player self, ZPackage pkg)
        {
            orig(self, pkg);

            if (Game.instance == null)
            {
                return;
            }

            self.UpdateKnownRecipesList();
        }
    }
}
