using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using MonoMod.RuntimeDetour;
using System.Reflection;
using static MonoMod.Cil.RuntimeILReferenceBag.FastDelegateInvokers;
using System.IO;


namespace Equillibrium
{
    [BepInDependency("etgmodding.etg.mtgapi")]
    [BepInPlugin(GUID, NAME, VERSION)]
    public class EquillibriumModule : BaseUnityPlugin
    {
        public const string GUID = "somebunny.etg.equillibrium";
        public const string NAME = "Equillibrium";
        public const string VERSION = "1.0.2";
        public const string TEXT_COLOR = "#ffa024";

       
        public void Start()
        {
            ETGModMainBehaviour.WaitForGameManagerStart(GMStart);
        }

        public void GMStart(GameManager g)
        {

                       
            EquillibriumConfig.configurationFile = Config;
            EquillibriumConfig.Init();

            if (EquillibriumConfig.advancedMode.Value == true)
            {
                SeparateClassForAHookBecauseGodHatesMe.Init();
                new Hook(typeof(LootData).GetMethod("GetItemsForPlayer", BindingFlags.Instance | BindingFlags.Public), typeof(EquillibriumModule).GetMethod("GetItemsForPlayerHook"));
            }
            g.StartCoroutine(DelayedStart());
        }


        public static float VanillaMultiplier()
        {
            return (float)((AmountOfModsOn() * EquillibriumConfig.VanillaChanceMultiplier.Value));
        }

        public static int AmountOfModsOn()
        {
            return EquillibriumConfig.ModAmountScaling.Value == true ? allIDs.Count : 1;
        }

        public static float DoHyperBolicScaling(float EffectOfOne, int Stack)
        {
            return 1 - 1/(1 + EffectOfOne * Stack);
        }


        public static List<PickupObject> GetItemsForPlayerHook(Func<LootData, PlayerController, int, GenericLootTable, System.Random, List<PickupObject>> orig, LootData self, PlayerController player, int tierShift = 0, GenericLootTable OverrideDropTable = null, System.Random generatorRandom = null)
        {
            List<PickupObject> p = orig(self, player, tierShift, OverrideDropTable, generatorRandom);
            foreach (PickupObject pp in p)
            {
                foreach (ModdedItemTracker moddedItemTracker in moddedItemTrackers)
                {
                    ReprocessWeights(moddedItemTracker, moddedItemTracker.ID == "gungeon" ? VanillaMultiplier() : 1, moddedItemTracker.ItemIDs.Contains(pp.PickupObjectId) == true ? false : true);
                }
            }
            return p;
        }



     

        public static void ReprocessWeights(ModdedItemTracker Tracker, float additionalMultiplier = 1,bool Tickdown = false)
        {
            //ETGModConsole.Log(Tracker.ID + ":");
            if (Tickdown == false) { Tracker.PickupCount = EquillibriumConfig.PickupCoefficient.Value + AmountOfModsOn(); }
            else if (Tracker.PickupCount > 1)
            {
               // ETGModConsole.Log("reducing pickup penalty value");
                Tracker.PickupCount--;
            }
            //ETGModConsole.Log("Current Pickup penalty: " + Tracker.PickupCount);


            float mult = 1 - (float)((float)Tracker.ItemIDs.Count / (float)allitems.Count);
            foreach (WeightedGameObject obj in GameManager.Instance.RewardManager.GunsLootTable.defaultItemDrops.elements)
            {
                if (Tracker.ItemIDs.Contains(obj.pickupId)) { obj.weight = (mult * additionalMultiplier)  / (float)Tracker.PickupCount; }
            }
            foreach (WeightedGameObject obj in GameManager.Instance.RewardManager.ItemsLootTable.defaultItemDrops.elements)
            {
                if (Tracker.ItemIDs.Contains(obj.pickupId)) { obj.weight = (mult * additionalMultiplier) / (float)Tracker.PickupCount; }
            }
            //ETGModConsole.Log((mult * additionalMultiplier) / (float)Tracker.PickupCount);
            //ETGModConsole.Log("=============");
        }






        public static List<ModdedItemTracker> moddedItemTrackers = new List<ModdedItemTracker>();
        public static List<PickupObject> allitems = new List<PickupObject>();
        public static List<string> allIDs = new List<string>();

        public IEnumerator DelayedStart()
        {
            //Initial weight processing starts

            yield return null;
            List<string> IDs = new List<string>();
            List<PickupObject> items = new List<PickupObject>();
            items.AddRange(Gungeon.Game.Items.Entries.ToList());
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].quality == PickupObject.ItemQuality.EXCLUDED)
                { items.Remove(items[i]); }
                else if (items[i].quality == PickupObject.ItemQuality.SPECIAL)
                { items.Remove(items[i]); }
                else if (items[i].quality == PickupObject.ItemQuality.COMMON)
                { items.Remove(items[i]); }
            }
            foreach (string o in Gungeon.Game.Items.AllIDs)
            {
                string ID = (o.Split(new string[] { ":" }, StringSplitOptions.None))[0];
                if (!IDs.Contains(ID)) { IDs.Add(ID); }
            }
            foreach (string ID in IDs)
            {
                List<int> itemIDs = new List<int>();

                foreach (string o in Gungeon.Game.Items.AllIDs)
                {
                    if (o.Contains(ID))
                    {
                        var p = Gungeon.Game.Items.Get(o);
                        if (p.quality == PickupObject.ItemQuality.EXCLUDED)
                        {
                            continue;
                        }
                        if (p.quality == PickupObject.ItemQuality.SPECIAL)
                        {
                            continue;
                        }
                        if (p.quality == PickupObject.ItemQuality.COMMON)
                        {
                            continue;
                        }
                        itemIDs.Add(p.PickupObjectId);
                    }
                }
                moddedItemTrackers.Add(new ModdedItemTracker()
                {
                    ID = ID,
                    ItemIDs = itemIDs,
                    PickupCount = 1
                });
            }
            allitems = items;
            allIDs = IDs;
            foreach (var ID in moddedItemTrackers)
            {
                ProcessnewItemDatabaseOfID(ID, ID.ID == "gungeon" ? VanillaMultiplier() : 1);
            }
            Log($"{NAME} v{VERSION} started successfully.", TEXT_COLOR);
            yield break;
        }

        public static void ProcessnewItemDatabaseOfID(ModdedItemTracker ID,  float additionalMultiplier = 1)
        {
           //Debug.Log($"{ID.ItemIDs.Count} | {allitems.Count} | {(float)((float)ID.ItemIDs.Count / (float)allitems.Count)} {1-(float)((float)ID.ItemIDs.Count / (float)allitems.Count)}");
            float mult = 1 - (float)((float)ID.ItemIDs.Count / (float)allitems.Count);
            var entries = GameManager.Instance.RewardManager.ItemsLootTable.defaultItemDrops.elements.Where(self => ID.ItemIDs.Contains(self.pickupId)).ToList();
            entries.AddRange(GameManager.Instance.RewardManager.GunsLootTable.defaultItemDrops.elements.Where(self => ID.ItemIDs.Contains(self.pickupId)).ToList());

            foreach (var entry in entries)
            {
                entry.weight *= (mult * additionalMultiplier);
                //Debug.Log(entry.weight);
            }
        }
        
        

        public static void Log(string text, string color="FFFFFF")
        {
            ETGModConsole.Log($"<color={color}>{text}</color>");
        }
    }
    public class ModdedItemTracker
    {
        public List<int> ItemIDs;
        public string ID;
        public int PickupCount = 0;
    }
}
