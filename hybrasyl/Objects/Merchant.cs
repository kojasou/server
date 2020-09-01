﻿/*
 * This file is part of Project Hybrasyl.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the Affero General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful, but
 * without ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE. See the Affero General Public License
 * for more details.
 *
 * You should have received a copy of the Affero General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 *
 * (C) 2020 ERISCO, LLC 
 *
 * For contributors and individual authors please refer to CONTRIBUTORS.MD.
 * 
 */
 
using Hybrasyl.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Hybrasyl.Objects
{
    public class MerchantInventoryItem
    {
        

        public Xml.Item Item { get; private set; }
        public uint OnHand { get; set; }
        public uint RestockAmount { get; private set; }
        public int RestockInterval { get; private set; }
        public DateTime LastRestock { get; set; }

        public MerchantInventoryItem(Xml.Item item, uint onHand, uint restockAmount, int restockInterval, DateTime lastRestock)
        {
            Item = item;
            OnHand = onHand;
            RestockAmount = restockAmount;
            RestockInterval = restockInterval;
            LastRestock = lastRestock;
        }

    }


    public class Merchant : Creature
    {
        private readonly object inventoryLock = new object();

        public bool Ready;
        //public npc Data;
        public Xml.NpcRoleList Roles { get; set; }
        public MerchantJob Jobs { get; set; }
        public List<MerchantInventoryItem> MerchantInventory { get; set; }

        public Merchant()
            : base()
        {
            Ready = false;
        }

        public uint GetOnHand(string itemName)
        {
            lock(inventoryLock)
            {
                return MerchantInventory.FirstOrDefault(x => x.Item.Name == itemName).OnHand;
            }
        }

        public void ReduceInventory(string itemName, uint quantity)
        {
            lock(inventoryLock)
            {
                MerchantInventory.FirstOrDefault(x => x.Item.Name == itemName).OnHand -= quantity;
            }
        }

        public void RestockInventory()
        {
            lock(inventoryLock)
            {
                if (MerchantInventory != null)
                {
                    foreach(var inventoryItem in MerchantInventory)
                    {
                        if (inventoryItem.LastRestock.AddMinutes(inventoryItem.RestockInterval) < DateTime.Now)
                        {
                            inventoryItem.OnHand = inventoryItem.RestockAmount;
                            inventoryItem.LastRestock = DateTime.Now;
                        }
                    }
                }
            }
        }

        public List<MerchantInventoryItem> GetOnHandInventory()
        {
            var ret = new List<MerchantInventoryItem>();
            lock (inventoryLock)
            {
                if (MerchantInventory != null)
                {
                    for (var i = 0; i < MerchantInventory.Count; i++)
                    {
                        ret.Add(MerchantInventory[i]);
                    }
                }
            }
            return ret;
        }

        // Currently, NPCs can not be healed or damaged in any way whatsoever
        public override void Heal(double heal, Creature source = null) { return; }
        public override void Damage(double damage, Xml.Element element = Xml.Element.None, Xml.DamageType damageType = Xml.DamageType.Direct, Xml.DamageFlags damageFlags = Xml.DamageFlags.None, Creature attacker = null, bool onDeath = true) { return; }

        public void OnSpawn()
        {
            if (Roles != null && Roles.Vend != null)
            {
                MerchantInventory = new List<MerchantInventoryItem>();

                lock (inventoryLock)
                {
                    foreach(var item in Roles.Vend.Items)
                    {
                        var worldItem = Game.World.WorldData.GetByIndex<Xml.Item>(item.Name);
                        MerchantInventory.Add(new MerchantInventoryItem(worldItem, (uint)item.Quantity, (uint)item.Quantity, item.Restock, DateTime.Now));
                    }
                }
            }

                Script script;
            // Do we have a script? If so, get it and run OnSpawn.
            if (World.ScriptProcessor.TryGetScript(Name, out script))
            {
                Script = script;
                Script.AssociateScriptWithObject(this);
                // Clear existing pursuits, in case the OnSpawn crashes / has a bug
                ResetPursuits();
                Ready = Script.ExecuteFunction("OnSpawn");
            }
            else
                Ready = true;
        }

        public override void OnHear(VisibleObject speaker, string text, bool shout = false)
        {
            if (speaker == this)
                return;

            if (!Ready)
                OnSpawn();

            if (Script != null)
            {
                Script.SetGlobalValue("text", text);
                Script.SetGlobalValue("shout", shout);

                if (speaker is User user)
                    Script.ExecuteFunction("OnHear", new HybrasylUser(user));
                else
                    Script.ExecuteFunction("OnHear", new HybrasylWorldObject(speaker));
            }
        }

        public override void OnClick(User invoker)
        {
            if (!Ready)
                OnSpawn();

            if (Script != null && Script.HasFunction("OnClick"))
               Script.ExecuteFunction("OnClick", new HybrasylUser(invoker));
            else
               DisplayPursuits(invoker);
        }

        public override void AoiEntry(VisibleObject obj)
        {
            base.AoiEntry(obj);
            if (Script != null)
            {
                Script.ExecuteFunction("OnEntry", new HybrasylWorldObject(obj));
            }
        }

        public override void AoiDeparture(VisibleObject obj)
        {
            base.AoiDeparture(obj);
            if (Script != null)
            {
                Script.ExecuteFunction("OnLeave", new HybrasylWorldObject(obj));
            }
        }

        public override void ShowTo(VisibleObject obj)
        {
            if (obj is User)
            {
                var user = obj as User;
                var npcPacket = new ServerPacket(0x07);
                npcPacket.WriteUInt16(0x01); // Number of mobs in this packet
                npcPacket.WriteUInt16(X);
                npcPacket.WriteUInt16(Y);
                npcPacket.WriteUInt32(Id);
                npcPacket.WriteUInt16((ushort)(Sprite + 0x4000));
                npcPacket.WriteByte(0);
                npcPacket.WriteByte(0);
                npcPacket.WriteByte(0);
                npcPacket.WriteByte(0);
                npcPacket.WriteByte((byte)Direction);
                npcPacket.WriteByte(0);

                npcPacket.WriteByte(2); // Dot color. 0 = monster, 1 = nonsolid monster, 2=NPC
                npcPacket.WriteString8(Name);
                user.Enqueue(npcPacket);
            }
        }
    }

    [Flags]
    public enum MerchantJob
    {
        Vend = 0x01,
        Bank = 0x02,
        Skills = 0x04,
        Spells = 0x08,
        Repair = 0x10,
        Post = 0x20
    }

    public enum MerchantMenuItem : ushort
    {
        MainMenu = 0xFF00,

        BuyItemMenu = 0xFF01,
        SellItemMenu = 0xFF02,

        WithdrawItemMenu = 0xFF03,
        WithdrawGoldMenu = 0xFF04,
        DepositItemMenu = 0xFF05,
        DepositGoldMenu = 0xFF06,

        LearnSkillMenu = 0xFF07,
        LearnSpellMenu = 0xFF08,
        ForgetSkillMenu = 0xFF09,
        ForgetSpellMenu = 0xFF0A,

        RepairItemMenu = 0xFF0B,
        RepairAllItems = 0xFF0C,

        SendParcelMenu = 0xFF0D,
        SendLetterMenu = 0xFF0E,
        ReceiveParcel = 0xFF0F,

        BuyItem = 0xFF10,
        BuyItemQuantity = 0xFF11,
        BuyItemAccept = 0xFF12,
        SellItem = 0xFF13,
        SellItemQuantity = 0xFF14,
        SellItemConfirm = 0xFF15,
        SellItemAccept = 0xFF16,

        WithdrawItem = 0xFF20,
        WithdrawItemQuantity = 0xFF21,
        DepositItem = 0xFF22,
        DepositItemQuantity = 0xFF23,
        WithdrawGoldQuantity = 0xFF24,
        DepositGoldQuantity = 0xFF25,

        LearnSkill = 0xFF30,
        LearnSkillAccept = 0xFF31,
        LearnSpell = 0xFF32,
        LearnSpellAccept = 0xFF33,
        ForgetSkill = 0xFF34,
        ForgetSkillAccept = 0xFF35,
        ForgetSpell = 0xFF36,
        ForgetSpellAccept = 0xFF37,
        LearnSkillAgree = 0xFF38,
        LearnSkillDisagree = 0xFF39,
        LearnSpellAgree = 0xFF3A,
        LearnSpellDisagree = 0xFF3B,


        RepairItem = 0xFF40,
        RepairItemAccept = 0xFF41,
        RepairAllItemsAccept = 0xFF43,

        SendParcel = 0xFF50,
        SendParcelRecipient = 0xFF51,
        SendLetter = 0xFF52,
        SendLetterRecipient = 0xFF53,
        SendParcelAccept = 0xFF54,
        SendParcelSuccess = 0xFF55,
        SendParcelFailure = 0xFF56,
        SendLetterAccept = 0xFF57,
        SendLetterSuccess = 0xFF59,
        SendLetterFailure = 0xFF59,

    }

    public enum MerchantDialogType : byte
    {
        Options = 0,
        OptionsWithArgument = 1,
        Input = 2,
        InputWithArgument = 3,
        MerchantShopItems = 4,
        UserInventoryItems = 5,
        MerchantSpells = 6,
        MerchantSkills = 7,
        UserSpellBook = 8,
        UserSkillBook = 9
    }

    public enum MerchantDialogObjectType : byte
    {
        Merchant = 1
    }

    public struct MerchantOptions
    {
        public byte OptionsCount => Convert.ToByte(Options.Count);
        public List<MerchantDialogOption> Options;
    }

    public struct MerchantOptionsWithArgument
    {
        public byte ArgumentLength => Convert.ToByte(Argument.Length);
        public string Argument;
        public byte OptionsCount => Convert.ToByte(Options.Count);
        public List<MerchantDialogOption> Options;
    }

    public struct MerchantDialogOption
    {
        public byte Length => Convert.ToByte(Text.Length);
        public string Text;
        public ushort Id;
    }

    public struct MerchantInput
    {
        public ushort Id;
    }

    public struct MerchantInputWithArgument
    {
        public byte ArgumentLength => Convert.ToByte(Argument.Length);
        public string Argument;
        public ushort Id;
    }

    public struct MerchantShopItems
    {
        public ushort Id;
        public ushort ItemsCount => Convert.ToUInt16(Items.Count);
        public List<MerchantShopItem> Items;
    }

    public struct MerchantShopItem
    {
        public ushort Tile;
        public byte Color;
        public uint Price;
        public byte NameLength => Convert.ToByte(Name.Length);
        public string Name;
        public byte DescriptionLength => Convert.ToByte(Description.Length);
        public string Description;
    }

    public struct UserInventoryItems
    {
        public ushort Id;
        public byte InventorySlotsCount => Convert.ToByte(InventorySlots.Count);
        public List<byte> InventorySlots;
    }

    public struct UserSkillBook
    {
        public ushort Id;
    }

    public struct UserSpellBook
    {
        public ushort Id;
    }

    public struct MerchantSpells
    {
        public ushort Id;
        public ushort SpellsCount => Convert.ToUInt16(Spells.Count());
        public byte IconType;
        public List<MerchantSpell> Spells;
    }

    public struct MerchantSpell
    {
        public byte IconType;
        public byte Icon;
        public byte Color;
        public byte NameLength => Convert.ToByte(Name.Length);
        public string Name;
    }

    public struct MerchantSkills
    {
        public ushort Id;
        public ushort SkillsCount => Convert.ToUInt16(Skills.Count());
        public byte IconType;
        public List<MerchantSkill> Skills;
    }

    public struct MerchantSkill
    {
        public byte IconType;
        public byte Icon;
        public byte Color;
        public byte NameLength => Convert.ToByte(Name.Length);
        public string Name;
    }




    public delegate void MerchantMenuHandlerDelegate(User user, Merchant merchant, ClientPacket packet);

    public class MerchantMenuHandler
    {
        public MerchantJob RequiredJob { get; set; }
        public MerchantMenuHandlerDelegate Callback { get; set; }
        public MerchantMenuHandler(MerchantJob requiredJob, MerchantMenuHandlerDelegate callback)
        {
            RequiredJob = requiredJob;
            Callback = callback;
        }
    }
}
