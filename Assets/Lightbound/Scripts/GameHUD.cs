using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles the HUD (Light reserve bar, Photonic Mass size readout, status messages),
/// the equidistant low-opacity parabolic throwing trajectory arc, and the Level Complete screen.
/// </summary>
public class GameHUD : MonoBehaviour
{
    static float _runStartTime;
    static int _orbsThrown;
    static int _orbsRecalled;
    static int _shardsCollected;
    static int _deaths;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ResetRunStats();

    public static void ResetRunStats()
    {
        _runStartTime = Time.time;
        _orbsThrown = 0;
        _orbsRecalled = 0;
        _shardsCollected = 0;
        _deaths = 0;
    }

    public static void RecordOrbThrown() => _orbsThrown++;
    public static void RecordOrbRecalled() => _orbsRecalled++;
    public static void RecordShardCollected() => _shardsCollected++;
    public static void RecordDeath() => _deaths++;

    string _msg;
    float _msgUntil, _flashAt = -10f;
    float _completionTime;
    GUIStyle _style;
    static Texture2D _dot;

    public void Show(string msg, float seconds = 2f)
    {
        _msg = msg;
        _msgUntil = Time.time + seconds;
    }

    public void TriggerCheckpointFlash() => _flashAt = Time.time;

    public void RecordLevelComplete()
    {
        _completionTime = Mathf.Max(0.1f, Time.time - _runStartTime);
    }

    public void DrawHUD(Player player)
    {
        if (player == null) return;
        float k = Screen.height / 720f;
        if (_style == null) _style = new GUIStyle(GUI.skin.label) { richText = true };
        _style.fontSize = Mathf.RoundToInt(18 * k);
        _style.alignment = TextAnchor.UpperLeft;
        var white = Texture2D.whiteTexture;
        var old = GUI.color;

        if (Event.current.type == EventType.Repaint) DrawAim(player, k);

        // checkpoint flash
        float f = 1f - (Time.time - _flashAt) / 0.5f;
        if (f > 0f)
        {
            GUI.color = new Color(0.5f, 1f, 0.7f, 0.25f * f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        }

        // light bar
        float t = Mathf.Clamp01(player.lightAmount / player.maxLight);
        var bar = new Rect(20 * k, 20 * k, 220 * k, 16 * k);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(bar.x - 2 * k, bar.y - 2 * k, bar.width + 4 * k, bar.height + 4 * k), white);
        GUI.color = t > 0.3f ? new Color(1f, 0.92f, 0.6f) : Color.Lerp(new Color(1f, 0.35f, 0.25f), new Color(1f, 0.92f, 0.6f), t / 0.3f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), white);
        GUI.color = old;

        string stateName = player.IsHeavy ? "Heavy (Slam: S in air)" : (player.IsTiny ? "Tiny" : "Normal");
        GUI.Label(new Rect(bar.xMax + 10 * k, bar.y - 4 * k, 520 * k, 26 * k),
            $"Light {Mathf.CeilToInt(player.lightAmount)}  |  Size {player.CurrentBodyScale:F2}x ({stateName})", _style);

        // status line
        string status = null;
        if (!player.HasWon && Time.time < _msgUntil) status = _msg;
        else if (!player.HasWon && !player.HasLight) status = "Your light is gone. The dark hides you and lets you slip through shadow walls. Right-Click to recall orbs.";
        if (status != null) GUI.Label(new Rect(20 * k, 44 * k, 950 * k, 28 * k), status, _style);

        // controls, bottom-left
        _style.fontSize = Mathf.RoundToInt(14 * k);
        GUI.Label(new Rect(20 * k, Screen.height - 30 * k, 1400 * k, 24 * k),
                  "A/D: move    Space/W: jump    Left click: throw light (shrink)    Right click/E: recall light (grow)    S (air): heavy slam    R: checkpoint", _style);

        // Simple raw Level Complete screen
        if (player.HasWon)
        {
            DrawSimpleLevelComplete(player, k, white);
        }
        GUI.color = old;
    }

    // Equidistant aim trajectory with low opacity
    void DrawAim(Player player, float k)
    {
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (!player.showAim || player.HasWon || mouse == null || cam == null || player.orbTemplate == null) return;
        if (_dot == null) _dot = MakeCircle(32);

        Vector2 start = player.transform.position;
        Vector2 aim = ((Vector2)cam.ScreenToWorldPoint(mouse.position.ReadValue()) - start).normalized;
        float spawnDist = 0.45f * player.CurrentBodyScale + 0.25f;
        Vector2 p0 = start + aim * spawnDist;
        Vector2 v0 = aim * player.throwSpeed;
        Vector2 g = Physics2D.gravity * player.orbTemplate.gravityScale;

        float maxArcLen = Mathf.Max(player.aimLength, 8.5f);
        float spacing = Mathf.Clamp(player.aimSpacing, 0.4f, 0.75f);
        float orbRadius = 0.18f;
        var old = GUI.color;
        GUI.color = player.HasLight ? new Color(1f, 0.92f, 0.6f, 0.32f) : new Color(0.6f, 0.6f, 0.6f, 0.2f);
        float size = 6f * k;

        const float dt = 0.008f;
        Vector2 prevP = p0;
        float accumDist = 0f;
        float nextDotDist = spacing * 0.6f;
        int mask = player.groundMask | player.shadowMask;

        for (int stepIdx = 1; stepIdx <= 320 && accumDist < maxArcLen; stepIdx++)
        {
            float tSim = stepIdx * dt;
            if (tSim > 2.5f) break;
            Vector2 curP = p0 + v0 * tSim + 0.5f * g * (tSim * tSim);

            RaycastHit2D hit = Physics2D.CircleCast(prevP, orbRadius, curP - prevP, Vector2.Distance(prevP, curP), mask);
            if (hit.collider != null) curP = hit.centroid;

            float segLen = Vector2.Distance(prevP, curP);
            if (segLen > 0.0001f)
            {
                while (accumDist + segLen >= nextDotDist && nextDotDist <= maxArcLen)
                {
                    float frac = (nextDotDist - accumDist) / segLen;
                    Vector2 dotPos = Vector2.Lerp(prevP, curP, frac);
                    Blit(cam, dotPos, _dot, size);
                    nextDotDist += spacing;
                }
            }

            accumDist += segLen;
            prevP = curP;
            if (hit.collider != null) break;
        }
        GUI.color = old;
    }

    void DrawSimpleLevelComplete(Player player, float k, Texture2D white)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);

        float boxW = 420 * k;
        float boxH = 260 * k;
        Rect box = new Rect((Screen.width - boxW) * 0.5f, (Screen.height - boxH) * 0.5f, boxW, boxH);

        GUI.color = new Color(0.12f, 0.13f, 0.16f, 0.95f);
        GUI.DrawTexture(box, white);

        var center = new GUIStyle(_style) { alignment = TextAnchor.MiddleCenter };
        GUI.color = Color.white;

        center.fontSize = Mathf.RoundToInt(26 * k);
        GUI.Label(new Rect(box.x, box.y + 20 * k, box.width, 36 * k), "<b>Level Complete!</b>", center);

        center.fontSize = Mathf.RoundToInt(16 * k);
        GUI.Label(new Rect(box.x, box.y + 68 * k, box.width, 26 * k), $"Time: {_completionTime:0.0}s", center);
        GUI.Label(new Rect(box.x, box.y + 96 * k, box.width, 26 * k), $"Final Light: {Mathf.CeilToInt(player.lightAmount)}  (Size {player.CurrentBodyScale:F2}x)", center);
        GUI.Label(new Rect(box.x, box.y + 124 * k, box.width, 26 * k), $"Orbs Thrown: {_orbsThrown}   |   Recalled: {_orbsRecalled}", center);
        GUI.Label(new Rect(box.x, box.y + 152 * k, box.width, 26 * k), $"Respawns: {_deaths}", center);

        Rect btn = new Rect(box.x + (box.width - 200 * k) * 0.5f, box.yMax - 54 * k, 200 * k, 36 * k);
        GUI.color = new Color(0.28f, 0.3f, 0.36f, 1f);
        GUI.DrawTexture(btn, white);
        GUI.color = Color.white;
        center.fontSize = Mathf.RoundToInt(15 * k);
        GUI.Label(btn, "Play Again (R / Space)", center);

        if (Event.current != null && Event.current.type == EventType.MouseDown && btn.Contains(Event.current.mousePosition))
        {
            Event.current.Use();
            player.RestartFromBeginning();
        }
    }

    static void Blit(Camera cam, Vector2 world, Texture2D tex, float size)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        GUI.DrawTexture(new Rect(s.x - size * 0.5f, Screen.height - s.y - size * 0.5f, size, size), tex);
    }

    static Texture2D MakeCircle(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float r = n * 0.5f;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
