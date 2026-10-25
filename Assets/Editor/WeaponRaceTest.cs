using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Edit-mode checks for weapon types and races (no Play mode needed):
//   - generates hundreds of orders per race and checks the tendencies (elves: daggers + Balance, dwarves:
//     hammers + Durability) without anyone being locked to one weapon;
//   - order wording, the "right weapon type" rule, hammers never asking for Sharpness;
//   - per-type grades, labels, save/load of weapon type + race;
//   - every weapon's forging phases and display model build, and every race's customer model.
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod WeaponRaceTest.Run -quit   (logs [WEAPON] lines)
public static class WeaponRaceTest
{
    private static bool ok = true;

    private static void Check(bool condition, string what)
    {
        Debug.Log($"[WEAPON] {(condition ? "ok  " : "FAIL")} {what}");
        ok &= condition;
    }

    public static void Run()
    {
        ok = true;
        Random.InitState(7);
        OrderTendencies();
        OrderRules();
        GradesAndHistory();
        Models();
        Debug.Log($"[WEAPON] RESULT: {(ok ? "PASS" : "FAIL")}");
    }

    private static void OrderTendencies()
    {
        const int N = 600;
        var weaponShare = new Dictionary<Race, Dictionary<WeaponType, float>>();
        var statShare = new Dictionary<Race, (float dur, float bal, float sharp, float strongMetal)>();
        foreach (Race race in System.Enum.GetValues(typeof(Race)))
        {
            var counts = new Dictionary<WeaponType, int>();
            foreach (WeaponType w in System.Enum.GetValues(typeof(WeaponType))) counts[w] = 0;
            int dur = 0, bal = 0, sharp = 0, metal = 0;
            for (int i = 0; i < N; i++)
            {
                var o = CustomerOrder.Generate(5, null, race);
                counts[o.weaponType]++;
                if (o.minDurability > 0f) dur++;
                if (o.minBalance > 0f) bal++;
                if (o.minSharpness > 0f) sharp++;
                if (o.requiredMaterial == "Steel" || o.requiredMaterial == "Iron") metal++;
                if (i < 3) Debug.Log($"[WEAPON] {race} order: \"{o.requestText}\"  -> {o.NeedSummary()}{(o.requiredMaterial != null ? ", " + o.requiredMaterial : "")}");
            }
            weaponShare[race] = counts.ToDictionary(p => p.Key, p => p.Value / (float)N);
            statShare[race] = (dur / (float)N, bal / (float)N, sharp / (float)N, metal / (float)N);
            Debug.Log($"[WEAPON] {race}: " + string.Join("  ", counts.Select(p => $"{p.Key} {p.Value * 100 / N}%")) +
                      $"  |  needs Durability {dur * 100 / N}%  Balance {bal * 100 / N}%  Sharpness {sharp * 100 / N}%  hard metal {metal * 100 / N}%");
        }

        var h = weaponShare[Race.Human];
        var e = weaponShare[Race.Elf];
        var d = weaponShare[Race.Dwarf];
        Check(e.OrderByDescending(p => p.Value).First().Key == WeaponType.Dagger, "elves ask for daggers more than anything else");
        Check(e[WeaponType.Dagger] > h[WeaponType.Dagger] + 0.1f, "elves ask for daggers far more often than humans");
        Check(d.OrderByDescending(p => p.Value).First().Key == WeaponType.Hammer, "dwarves ask for hammers more than anything else");
        Check(d[WeaponType.Hammer] > h[WeaponType.Hammer] + 0.1f, "dwarves ask for hammers far more often than humans");
        Check(statShare[Race.Elf].bal > statShare[Race.Human].bal + 0.1f, "elves care about Balance more often than humans");
        Check(statShare[Race.Dwarf].dur > statShare[Race.Human].dur + 0.1f, "dwarves care about Durability more often than humans");
        Check(statShare[Race.Dwarf].strongMetal > statShare[Race.Human].strongMetal, "dwarves insist on hard metal more often");
        foreach (var race in weaponShare.Keys)
            Check(weaponShare[race].Count(p => p.Value > 0.02f) >= 3, $"{race}s aren't locked to one weapon ({weaponShare[race].Count(p => p.Value > 0.02f)} kinds requested)");
        Check(e[WeaponType.Sword] > 0.02f, "an elf still sometimes wants a sword");
        Check(d[WeaponType.Sword] + d[WeaponType.Spear] > 0.05f, "a dwarf still sometimes wants a sword or a spear");
    }

    private static void OrderRules()
    {
        bool wording = true, hammerNoSharp = true, mentionsWeapon = true;
        for (int i = 0; i < 400; i++)
        {
            var o = CustomerOrder.Generate(1 + i % 8);
            o.deadlineDay = 10;
            string paper = o.PaperText(1 + i % 8);
            if (o.requestText.Contains("{") || paper.Contains("{")) { wording = false; Debug.Log("[WEAPON] unfilled placeholder: " + o.requestText); }
            if (!o.requestText.ToLower().Contains(o.Weapon.noun)) { mentionsWeapon = false; Debug.Log("[WEAPON] doesn't name the weapon: " + o.requestText); }
            if (!paper.Contains($"Wants: {o.Weapon.WithArticle}")) mentionsWeapon = false;
            if (o.weaponType == WeaponType.Hammer && o.minSharpness > 0f) hammerNoSharp = false;
        }
        Check(wording, "no order text is left with a {placeholder}");
        Check(mentionsWeapon, "every order names its weapon, in the request and on the order paper");
        Check(hammerNoSharp, "hammer orders never ask for Sharpness");

        var order = CustomerOrder.Generate(5, null, Race.Elf);
        order.weaponType = WeaponType.Dagger;
        order.requiredMaterial = null;
        order.minDurability = 40; order.minBalance = 80; order.minSharpness = 60;
        var dagger = new WorkpieceData { weaponType = WeaponType.Dagger, materialName = "Iron", durability = 50, balance = 90, sharpness = 70 };
        var sword = dagger.Clone();
        sword.weaponType = WeaponType.Sword;
        Check(order.IsFulfilled(dagger), "a dagger that meets the numbers fulfils a dagger order");
        Check(!order.IsFulfilled(sword), "a sword with the very same numbers does not fulfil a dagger order");
        Check(order.Title.Contains("elven"), $"an elf's title says so ({order.Title})");
    }

    private static void GradesAndHistory()
    {
        var hammer = new WorkpieceData { weaponType = WeaponType.Hammer, materialName = "Steel", durability = 95, balance = 80, sharpness = 0 };
        var hammerAsSword = hammer.Clone();
        hammerAsSword.weaponType = WeaponType.Sword;
        Check(hammer.Grade == "S" || hammer.Grade == "A", $"a tough, well-balanced hammer grades well without any Sharpness ({hammer.Grade}; the same numbers as a sword: {hammerAsSword.Grade})");
        Check(Weapons.Get(WeaponType.Hammer).SharpnessText(0) == "N/A" && Weapons.Get(WeaponType.Dagger).SharpnessText(64) == "64", "Sharpness reads N/A for a hammer, a number for a dagger");
        Check(new WorkpieceData { durability = 60, balance = 60, sharpness = 60 }.Grade == "B", "a sword's grade is unchanged (plain average)");

        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tb_weapon_test_save.json");
        string oldEnv = System.Environment.GetEnvironmentVariable("TB_SAVE_PATH");
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", path);
        try
        {
            var shop = new Workshop();
            var h = shop.history;
            var d = h.RegisterForged(new WorkpieceData { weaponType = WeaponType.Dagger, materialName = "Iron", itemName = "Iron Dagger", durability = 62, balance = 94, sharpness = 88 }, 1);
            var m = h.RegisterForged(new WorkpieceData { weaponType = WeaponType.Hammer, materialName = "Steel", itemName = "Steel Hammer", durability = 96, balance = 58 }, 1);
            Check(d.Label == "Dagger #0001" && m.Label == "Hammer #0002", $"labels carry the weapon type ({d.Label}, {m.Label})");
            var elfOrder = CustomerOrder.Generate(1, null, Race.Elf);
            elfOrder.weaponType = WeaponType.Dagger;
            var elf = h.AddCustomer(elfOrder, true);
            h.Sell(d, elf, elfOrder, 1);
            SaveSystem.Save(shop, null);
            var loaded = new Workshop();
            bool read = SaveSystem.TryLoad(loaded, out _);
            var ld = loaded.history.Find(1);
            var lm = loaded.history.Find(2);
            Check(read && ld != null && ld.sword.weaponType == WeaponType.Dagger && lm.sword.weaponType == WeaponType.Hammer, "weapon types survive save/load");
            Check(read && loaded.history.Owner(ld)?.race == Race.Elf && loaded.history.Owner(ld).Title.Contains("elven"), "a customer's race survives save/load");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", oldEnv);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private static void Models()
    {
        foreach (WeaponType type in System.Enum.GetValues(typeof(WeaponType)))
        {
            bool phases = true;
            for (int p = 0; p < LowPoly.PhaseCount; p++)
            {
                var m = LowPoly.WeaponPhase(type, p);
                if (m == null || m.mesh.vertexCount == 0) phases = false;
            }
            var display = Weapons.DisplayModel(type);
            var b = display.mesh.bounds;
            bool shaped = Mathf.Abs(b.min.y) < 0.02f && b.max.y > 0.2f && display.paints.Length == display.mesh.subMeshCount;
            float shoulder = LowPoly.WeaponShoulder(type);
            var finished = LowPoly.WeaponPhase(type, LowPoly.PhaseCount - 1).mesh.bounds;
            Check(phases && shaped, $"{type}: 5 forging phases, display model {b.size.x:0.00} x {b.max.y:0.00} (handle end at y={b.min.y:0.00}), " +
                                    $"head reaches {-finished.min.z:0.00} forward, metal {finished.max.z:0.00} back, handle from {shoulder:0.00}, " +
                                    $"{Weapons.Get(type).hilt.Length} handle parts ({Weapons.Get(type).PartNames})");
        }
        var hu = NpcProportions.For(Race.Human).Height; // the townsfolk and customers are NpcBody characters
        var el = NpcProportions.For(Race.Elf).Height;
        var dw = NpcProportions.For(Race.Dwarf).Height;
        Check(dw < hu - 0.2f && el > hu, $"a dwarf is shorter and an elf taller than a human ({dw:0.00} / {hu:0.00} / {el:0.00})");
    }
}
