using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The time system's balance, checked against the real numbers (TimeSettings + GameTime), no scene needed:
//   - work durations per weapon / repair are in their target ranges, bigger weapons take longer, repairs are shorter
//   - TEST A: a sword from 08:00 through every stage (GameTime.Spend, as the stations do) ends 2+ hours later
//   - TEST B: fast and slow players - the minigames' real seconds don't change the in-game time a weapon takes
//   - TEST D: an all-out forger can't get anywhere near dozens a day; a normal day holds 2-4 weapons
//   - TEST E: a 5-sword contract needs several days of work
//   - customers: 3-4 arrivals a day, spread over customer hours, different every day; deadlines (normal, rush, contract)
//   - upgrades shorten work only a little (diminishing)
// A simulated normal day is written to the log as a schedule. Run: Unity.exe -batchmode -quit -projectPath <p>
// -executeMethod TimeBalanceTest.Run   ([TIME] lines, RESULT at the end)
public static class TimeBalanceTest
{
    private static int failures;
    private static TimeSettings S => TimeSettings.Current;

    private static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Debug.Log($"[TIME] {(ok ? "ok  " : "FAIL")} {what}");
    }

    private static string C(float h) => GameTime.Clock(h);
    private static string D(float m) => GameTime.Duration(m);

    public static void Run()
    {
        failures = 0;
        var shop = new Workshop();

        // ---- durations ----------------------------------------------------------------------------------------------
        var total = new Dictionary<WeaponType, float>();
        foreach (var w in Weapons.All) total[w.type] = GameTime.WeaponMinutes(shop, w.type);
        bool In(WeaponType t, float lo, float hi) => total[t] >= lo && total[t] <= hi;
        Check(In(WeaponType.Dagger, 80, 100) && In(WeaponType.Sickle, 90, 120) && In(WeaponType.Hammer, 105, 135) &&
              In(WeaponType.Sword, 120, 150) && In(WeaponType.Spear, 120, 150),
              $"each weapon takes hours, in its target range: dagger {D(total[WeaponType.Dagger])}, sickle {D(total[WeaponType.Sickle])}, hammer {D(total[WeaponType.Hammer])}, sword {D(total[WeaponType.Sword])}, spear {D(total[WeaponType.Spear])}");
        Check(total[WeaponType.Dagger] < total[WeaponType.Sickle] && total[WeaponType.Sickle] < total[WeaponType.Hammer] &&
              total[WeaponType.Hammer] < total[WeaponType.Sword] && total[WeaponType.Spear] > total[WeaponType.Hammer],
              "bigger, more complicated weapons take longer (dagger < sickle < hammer < sword; spear ~ sword)");
        var sw = S.WorkFor(WeaponType.Sword);
        Check(sw.furnace >= 20 && sw.furnace <= 30 && sw.anvil >= 35 && sw.anvil <= 50 && sw.quench >= 15 && sw.quench <= 25 &&
              sw.handle >= 20 && sw.handle <= 30 && sw.grind >= 25 && sw.grind <= 40 && sw.finish >= 5 && sw.finish <= 15,
              $"sword stages: furnace {sw.furnace}m, anvil {sw.anvil}m, quench {sw.quench}m, handle {sw.handle}m, grind {sw.grind}m, finish {sw.finish}m");
        float Rep(RepairKind k) => GameTime.RepairMinutes(shop, k);
        Check(Rep(RepairKind.Sharpening) >= 20 && Rep(RepairKind.Sharpening) <= 30 && Rep(RepairKind.Handle) >= 30 && Rep(RepairKind.Handle) <= 45 &&
              Rep(RepairKind.BentBlade) >= 45 && Rep(RepairKind.BentBlade) <= 60 && Rep(RepairKind.Moderate) >= 50 && Rep(RepairKind.Moderate) <= 70 &&
              Rep(RepairKind.Major) >= 60 && Rep(RepairKind.Major) <= 90 && Rep(RepairKind.Major) < total[WeaponType.Dagger],
              $"repairs are quicker than forging: sharpening {D(Rep(RepairKind.Sharpening))}, handle {D(Rep(RepairKind.Handle))}, bent blade {D(Rep(RepairKind.BentBlade))}, moderate {D(Rep(RepairKind.Moderate))}, major {D(Rep(RepairKind.Major))}");

        // ---- TEST A: a whole sword through the stages, from 08:00 -------------------------------------------------------
        var go = GameTime.Create(shop);
        try
        {
            go.SetHour(8f);
            foreach (WorkStage st in System.Enum.GetValues(typeof(WorkStage)))
                go.Spend(GameTime.StageMinutes(shop, WeaponType.Sword, st), st.ToString());
            float passed = (go.Hour - 8f) * 60f;
            Check(passed >= 120f && passed <= 150f, $"TEST A: a sword started at 08:00 is finished at {go.ClockText} ({D(passed)} of work)");

            // ---- TEST B: fast and slow hands - the same in-game time ---------------------------------------------------
            // Minigame seconds never reach the clock (only Spend does, at each stage's end); passive time runs only
            // while idle in the workshop, so what differs is how long each player stands about between jobs.
            float SwordFor(float realSecondsPerStage, float idleSecondsBefore)
            {
                go.SetHour(8f);
                float passive = idleSecondsBefore * S.passiveMinutesPerRealMinute / 60f;
                go.Spend(passive, "idle");
                foreach (WorkStage st in System.Enum.GetValues(typeof(WorkStage)))
                    go.Spend(GameTime.StageMinutes(shop, WeaponType.Sword, st), st.ToString()); // (realSecondsPerStage never enters)
                return (go.Hour - 8f) * 60f;
            }
            float fast = SwordFor(8f, 5f), slow = SwordFor(150f, 240f);
            Check(Mathf.Abs(fast - total[WeaponType.Sword]) < 1f && slow - fast < 16f,
                  $"TEST B: a fast player's sword takes {D(fast)}; a slow one (2.5 real minutes a stage, 4 idle minutes) {D(slow)} - speed doesn't skip work, thinking costs little");
        }
        finally { Object.DestroyImmediate(go.gameObject); }

        // ---- TEST D: how much can one day hold? ---------------------------------------------------------------------------
        foreach (var w in Weapons.All)
        {
            int n = AllOut(shop, w.type, out float endHour);
            Check(n <= (w.type == WeaponType.Dagger ? 8 : w.type == WeaponType.Sickle ? 7 : w.type == WeaponType.Hammer ? 6 : 5),
                  $"TEST D: forging {w.name.ToLower()}s non-stop from {C(S.dayStart)} (no customers, no breaks): {n} before the {C(S.newWeaponCutoff)} cutoff, last done {C(endHour)}");
        }
        var upgraded = new Workshop { furnaceLevel = 9, grindstoneLevel = 9, toolsLevel = 9 };
        int maxed = AllOut(upgraded, WeaponType.Sword, out _);
        Check(GameTime.WeaponMinutes(upgraded, WeaponType.Sword) >= 0.85f * total[WeaponType.Sword] && maxed <= 6,
              $"fully upgraded workshop: a sword still takes {D(GameTime.WeaponMinutes(upgraded, WeaponType.Sword))} ({maxed} a day at the very most) - upgrades help, never break the day");

        var normal = new List<int>();
        for (int day = 1; day <= 30; day++) normal.Add(NormalDay(shop, day, log: day == 4, out _));
        float avg = 0f; foreach (int n in normal) avg += n; avg /= normal.Count;
        Check(normal.TrueForAll(n => n >= 2 && n <= 4) && avg >= 2.5f && avg <= 3.8f,
              $"TEST D: a normal day (customers, a repair, winding down in the evening) holds {normal.Min()}-{normal.Max()} new weapons (average {avg:0.0} over 30 days)");

        // ---- TEST E: a bulk contract - five swords ----------------------------------------------------------------------
        var contract = new CustomerOrder { weaponType = WeaponType.Sword, quantity = 5, patienceDays = 3 };
        shop.Accept(contract, 6, 9.5f);
        float needed = 5 * total[WeaponType.Sword];
        float workday = (S.eveningStart - S.dayStart) * 60f;
        int days = ContractDays(shop, 5, 6);
        Check(contract.deadlineDay - 6 >= 2 && needed > workday && days >= 2 && 6 + days - 1 <= contract.deadlineDay,
              $"TEST E: 5 swords = {D(needed)} of forging, more than a whole working day ({D(workday)}); due {GameTime.DueText(contract)}; a normal smith finishes on day {6 + days - 1} ({days} days of work)");

        // ---- customers through the day -------------------------------------------------------------------------------------
        bool countOk = true, inHours = true, spread = true, varied = false;
        float[] first = GameTime.Arrivals(1, shop.timeSeed);
        for (int day = 1; day <= 200; day++)
        {
            var a = GameTime.Arrivals(day, shop.timeSeed);
            countOk &= a.Length >= S.newCustomersMin && a.Length <= S.newCustomersMax;
            for (int i = 0; i < a.Length; i++)
            {
                inHours &= a[i] >= S.customerStart && a[i] < S.customerEnd;
                if (i > 0) spread &= a[i] - a[i - 1] >= 0.75f;
            }
            if (day > 1 && (a.Length != first.Length || Mathf.Abs(a[0] - first[0]) > 0.05f)) varied = true;
        }
        Check(countOk && inHours && spread && varied,
              $"customers: {S.newCustomersMin}-{S.newCustomersMax} a day, all in customer hours, at least 45 min apart, different every day (day 1: {string.Join(", ", System.Array.ConvertAll(first, C))})");

        // ---- deadlines ----------------------------------------------------------------------------------------------------
        var plain = new CustomerOrder { weaponType = WeaponType.Dagger, patienceDays = 3 };
        shop.Accept(plain, 4, 11.33f);
        var rush = new CustomerOrder { weaponType = WeaponType.Sword, rush = true };
        shop.Accept(rush, 4, 11.33f);
        Check(plain.deadlineDay >= 6 && rush.deadlineDay == 5 && Mathf.Approximately(rush.DeadlineHour, S.deadlineHour) &&
              Mathf.Approximately(plain.acceptedHour, 11.33f) && rush.returnDay == 5 && rush.returnHour < rush.DeadlineHour,
              $"deadlines: accepted day 4 {C(11.33f)} - a normal order is due {GameTime.DueText(plain)} (generous), a rush order {GameTime.DueText(rush)}, its customer back ~{C(rush.returnHour)}");
        Check(!GameTime.IsOverdue(rush, 5, 17.9f) && GameTime.IsOverdue(rush, 5, 18f) && GameTime.IsOverdue(rush, 6, 9f), "an order is overdue from its deadline hour on");

        Debug.Log($"[TIME] RESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
    }

    // Forging one kind of weapon back to back from the start of the day until no new one may be begun.
    private static int AllOut(Workshop shop, WeaponType type, out float endHour)
    {
        float h = S.dayStart, each = GameTime.WeaponMinutes(shop, type) / 60f;
        int n = 0;
        while (h < S.newWeaponCutoff) { h = Mathf.Min(S.latestHour, h + each); n++; }
        endHour = h;
        return n;
    }

    // A normal player's day: customers come at their times (each is a talk), orders and the odd repair get done,
    // new work is only begun if it'll be finished by about 19:30, and some time goes on looking around.
    private static int NormalDay(Workshop shop, int day, bool log, out float endHour)
    {
        var rng = new System.Random(day * 31 + 7);
        var arrivals = GameTime.Arrivals(day, shop.timeSeed);
        int next = 0, forged = 0, queued = 1, repairs = rng.Next(0, 2), handOvers = 2;
        float h = S.dayStart;
        var lines = new List<string> { $"{C(h)}  start of day {day}" };
        WeaponType[] kinds = { WeaponType.Sword, WeaponType.Dagger, WeaponType.Sickle, WeaponType.Hammer, WeaponType.Spear };
        float Idle(float realSeconds) => realSeconds * S.passiveMinutesPerRealMinute / 60f / 60f;
        void Customers()
        {
            while (next < arrivals.Length && h >= arrivals[next] - (next == 0 ? S.earlyArrivalHours : 0f) && h < S.customerEnd)
            {
                h += Idle(20f) + S.orderTalkMinutes / 60f;
                lines.Add($"{C(h - S.orderTalkMinutes / 60f)}  customer - an order");
                next++;
                queued++;
            }
        }
        while (true)
        {
            h += Idle(60f + rng.Next(0, 90)); // a look round, the storage, the journal (mostly paused anyway)
            Customers();
            if (handOvers > 0 && h >= 11f + 3f * (2 - handOvers)) { h += S.handOverMinutes / 60f; handOvers--; lines.Add($"{C(h - S.handOverMinutes / 60f)}  a customer collects their order"); }
            var kind = kinds[rng.Next(kinds.Length)];
            float forge = GameTime.WeaponMinutes(shop, kind) / 60f;
            if (queued > 0 && h < S.newWeaponCutoff && h + forge <= 18.5f)
            {
                lines.Add($"{C(h)}-{C(h + forge)}  {kind}");
                h += forge; forged++; queued--;
                continue;
            }
            if (repairs > 0 && h < S.repairCutoff)
            {
                float r = GameTime.RepairMinutes(shop, (RepairKind)rng.Next(0, 5)) / 60f;
                lines.Add($"{C(h)}-{C(h + r)}  repair");
                h += r; repairs--;
                continue;
            }
            if (next < arrivals.Length && arrivals[next] < S.customerEnd && h < S.customerEnd) { h = Mathf.Max(h, arrivals[next] - S.earlyArrivalHours); continue; }
            break;
        }
        h += Idle(120f) + 0.25f; // the order book, the storage, planning tomorrow
        lines.Add($"{C(h)}  evening: storage, materials, End Day");
        endHour = h;
        if (log) Debug.Log("[TIME] a simulated normal day:\n" + string.Join("\n", lines));
        return forged;
    }

    // Days a normal smith takes over a contract of `count` swords, alongside a customer and a repair each day.
    private static int ContractDays(Workshop shop, int count, int startDay)
    {
        float each = GameTime.WeaponMinutes(shop, WeaponType.Sword) / 60f;
        int days = 0;
        while (count > 0 && days < 10)
        {
            days++;
            float h = S.dayStart + (days == 1 ? 1.5f : 0f) + 3 * S.orderTalkMinutes / 60f + GameTime.RepairMinutes(shop, RepairKind.Moderate) / 60f;
            while (count > 0 && h + each <= 19.5f) { h += each; count--; }
        }
        return days;
    }
}

internal static class ListMinMax
{
    public static int Min(this List<int> l) { int m = int.MaxValue; foreach (int v in l) m = Mathf.Min(m, v); return m; }
    public static int Max(this List<int> l) { int m = int.MinValue; foreach (int v in l) m = Mathf.Max(m, v); return m; }
}
