using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Visual check (Play mode, never touches the real save): hangs one of every weapon type on the rack and shoots
// the storage view with each tooltip logged, shows a customer of each race carrying a different weapon, and
// flips the journal to a page of each type. Screenshots -> Logs/show_*.png, [SHOW] log lines.
// Run: Unity.exe -batchmode -projectPath <p> -executeMethod WeaponShowcase.Run   (no -quit)
public static class WeaponShowcase
{
    private static ForgeGameManager manager;
    private static double started;
    private static bool running;

    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("TB_SAVE_PATH", "none");
        manager = null;
        running = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype1.unity");
        EditorApplication.isPlaying = true;
        EditorApplication.update += Tick;
        started = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 90) { Done("TIMEOUT"); return; }
        if (manager == null) manager = Object.FindAnyObjectByType<ForgeGameManager>();
        if (manager == null || running || EditorApplication.timeSinceStartup - started < 3) return;
        running = true;
        manager.StopAllCoroutines(); // pause the game loop (customers, turning) while we pose things
        manager.StartCoroutine(Show());
    }

    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Call(string method, params object[] args) => typeof(ForgeGameManager).GetMethod(method, Flags).Invoke(manager, args);
    private static T Field<T>(string name) => (T)typeof(ForgeGameManager).GetField(name, Flags).GetValue(manager);

    private static IEnumerator Show()
    {
        var h = manager.workshop.history;
        var ui = Field<ForgeUI>("ui");
        string[] metals = { "Steel", "Iron", "Copper", "Iron", "Steel" };
        int i = 0;
        foreach (WeaponType type in System.Enum.GetValues(typeof(WeaponType)))
        {
            var w = Weapons.Get(type);
            h.RegisterForged(new WorkpieceData
            {
                weaponType = type, materialName = metals[i], itemName = $"{metals[i]} {w.name}", weight = w.weight,
                durability = 50 + i * 9, balance = 90 - i * 8, sharpness = w.hasEdge ? 60 + i * 5 : 0,
            }, 1);
            i++;
        }

        // Storage view: every type on the rack.
        yield return manager.StartCoroutine((IEnumerator)Call("TurnTo", 180f, 0.3f));
        for (int f = 0; f < 5; f++) { Call("UpdateStorageUI"); yield return null; }
        Shot("show_storage");
        for (int s = 0; s < 5; s++) Debug.Log($"[SHOW] rack slot {s}: {ui.swordSlots[s].tooltip?.Replace("\n", " | ")}  rect={ui.swordSlots[s].rect.anchoredPosition}/{ui.swordSlots[s].rect.sizeDelta}");

        // A customer of each race, carrying someone's weapon.
        yield return manager.StartCoroutine((IEnumerator)Call("TurnTo", 90f, 0.3f));
        ui.ShowStorage(false);
        var customer = Field<Transform>("customer");
        var records = h.swords;
        var races = new[] { Race.Human, Race.Elf, Race.Dwarf };
        WeaponType[] carry = { WeaponType.Sword, WeaponType.Spear, WeaponType.Hammer };
        for (int r = 0; r < races.Length; r++)
        {
            var rec = records.Find(x => x.sword.weaponType == carry[r]);
            var order = CustomerOrder.Generate(1, null, races[r]);
            rec.status = SwordStatus.OwnedByCustomer; // just for the display
            Call("DressCustomer", order.archetype.outfit, order.hair, false, races[r]);
            Call("ShowCustomerSword", rec);
            customer.gameObject.SetActive(true);
            customer.position = new Vector3(2.9f, 0f, -3.5f);
            customer.rotation = Quaternion.LookRotation(Vector3.left);
            yield return null;
            Shot($"show_customer_{races[r].ToString().ToLower()}");
            Debug.Log($"[SHOW] {races[r]} customer ({order.Title}) carrying {rec.Label}");
            rec.status = SwordStatus.InStorage;
        }
        customer.gameObject.SetActive(false);

        // The journal, one page per weapon type.
        ui.ShowJournal(true);
        for (int p = 0; p < records.Count; p++)
        {
            Call("ShowJournalPage", p + 1); // page 0 is the Chronicle
            yield return null;
            Shot($"show_journal_{records[p].sword.weaponType.ToString().ToLower()}");
        }
        ui.ShowJournal(false);
        Done("done");
    }

    private static void Shot(string name)
    {
        var cam = Camera.main;
        var canvas = GameObject.Find("ForgeUI").GetComponent<Canvas>();
        var rt = new RenderTexture(1280, 720, 24);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        Canvas.ForceUpdateCanvases();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
    }

    private static void Done(string reason)
    {
        EditorApplication.update -= Tick;
        Debug.Log($"[SHOW] RESULT: {reason}");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
}
