using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages the Photonic Mass puzzle obstacles in the level (Squeeze Crevice Gate, Low-Gravity Sky Ledge,
/// and Cracked Monolith Wall) as well as Shard regeneration and contextual player hints.
/// </summary>
public class LevelObstacles : MonoBehaviour
{
    public readonly List<GameObject> CrackedBlocks = new List<GameObject>();
    readonly Dictionary<GameObject, float> _respawningShards = new Dictionary<GameObject, float>();

    public void SetupLevelObstacles()
    {
        CrackedBlocks.Clear();
        var levelRoot = GameObject.Find("Level");
        var groundParent = levelRoot != null ? levelRoot.transform.Find("Ground") : null;
        var shardsParent = levelRoot != null ? levelRoot.transform.Find("Shards") : null;
        if (groundParent == null || groundParent.childCount == 0) return;

        GameObject groundTemplate = groundParent.GetChild(0).gameObject;
        GameObject shardTemplate = (shardsParent != null && shardsParent.childCount > 0) ? shardsParent.GetChild(0).gameObject : null;

        // 1. Low overhang at x = 8.2 (0.62m gap underneath requiring Tiny Form)
        var overhangTf = groundParent.Find("SqueezeGate_Overhang");
        GameObject overhang = overhangTf != null
            ? overhangTf.gameObject
            : CloneGround(groundTemplate, groundParent, "SqueezeGate_Overhang",
                new Vector3(8.2f, 5.62f, 0f), new Vector3(1.8f, 6.0f, 1f), new Color(0.35f, 0.38f, 0.45f, 1f));
        overhang.transform.position = new Vector3(8.2f, 5.62f, 0f);
        var overhangSr = overhang.GetComponent<SpriteRenderer>();
        if (overhangSr != null) overhangSr.color = new Color(0.35f, 0.38f, 0.45f, 1f);
        var overhangCol = overhang.GetComponent<Collider2D>();
        if (overhangCol != null)
        {
            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                var ec = enemy.GetComponent<Collider2D>();
                if (ec != null) Physics2D.IgnoreCollision(overhangCol, ec, true);
            }
        }

        // 2. High low-gravity ledge at x = 57.5, y = 9.8
        var skyLedgeTf = groundParent.Find("SkyFloat_Ledge");
        GameObject skyLedge = skyLedgeTf != null
            ? skyLedgeTf.gameObject
            : CloneGround(groundTemplate, groundParent, "SkyFloat_Ledge",
                new Vector3(57.5f, 9.8f, 0f), new Vector3(3.6f, 0.7f, 1f), new Color(0.35f, 0.38f, 0.45f, 1f));
        var skySr = skyLedge.GetComponent<SpriteRenderer>();
        if (skySr != null) skySr.color = new Color(0.35f, 0.38f, 0.45f, 1f);
        if (shardTemplate != null && shardsParent != null && shardsParent.Find("Shard_SkyVault") == null)
        {
            var skyShard = Instantiate(shardTemplate, new Vector3(57.5f, 11.0f, 0f), Quaternion.identity, shardsParent);
            skyShard.name = "Shard_SkyVault";
        }

        // 3. Breakable Cracked Block at x = 91.5 (requires Heavy Form 65+ Light + Heavy Slam)
        var existingCracked = groundParent.Find("CrackedMonolith_Gate");
        GameObject cracked = existingCracked != null
            ? existingCracked.gameObject
            : CloneGround(groundTemplate, groundParent, "CrackedMonolith_Gate",
                new Vector3(91.5f, 4.1f, 0f), new Vector3(1.3f, 4.2f, 1f), new Color(0.75f, 0.5f, 0.25f, 1f));
        CrackedBlocks.Add(cracked);

        if (groundParent.Find("CrackedMonolith_Arch") == null)
        {
            CloneGround(groundTemplate, groundParent, "CrackedMonolith_Arch",
                new Vector3(91.5f, 7.7f, 0f), new Vector3(2.8f, 3.0f, 1f), new Color(0.35f, 0.38f, 0.45f, 1f));
        }
        if (shardTemplate != null && shardsParent != null && shardsParent.Find("Shard_HeavyPrep") == null)
        {
            var prepShard = Instantiate(shardTemplate, new Vector3(89.5f, 3.2f, 0f), Quaternion.identity, shardsParent);
            prepShard.name = "Shard_HeavyPrep";
        }
    }

    public void RegisterCollectedShard(GameObject shard, float respawnDelay = 6f)
    {
        var col = shard.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        foreach (var sr in shard.GetComponentsInChildren<SpriteRenderer>()) sr.enabled = false;
        foreach (var l in shard.GetComponentsInChildren<Light2D>()) l.enabled = false;
        _respawningShards[shard] = Time.time + respawnDelay;
    }

    public void TickRespawningShards()
    {
        if (_respawningShards.Count == 0) return;
        List<GameObject> ready = null;
        foreach (var kv in _respawningShards)
        {
            if (Time.time >= kv.Value)
            {
                if (ready == null) ready = new List<GameObject>();
                ready.Add(kv.Key);
            }
        }
        if (ready != null)
        {
            foreach (var shard in ready)
            {
                _respawningShards.Remove(shard);
                if (shard == null) continue;
                var col = shard.GetComponent<Collider2D>();
                if (col != null) col.enabled = true;
                foreach (var sr in shard.GetComponentsInChildren<SpriteRenderer>()) sr.enabled = true;
                foreach (var l in shard.GetComponentsInChildren<Light2D>()) l.enabled = true;
            }
        }
    }

    public void CheckHints(Player player)
    {
        if (player == null) return;
        Vector2 p = player.transform.position;
        if (p.x > 5.2f && p.x < 8.0f && p.y < 4.5f && player.CurrentBodyScale > 0.66f)
        {
            player.ShowMessage("Low gap: throw light (Left-Click) to shrink, then Right-Click to recall", 0.5f);
        }
        for (int i = 0; i < CrackedBlocks.Count; i++)
        {
            var b = CrackedBlocks[i];
            if (b != null && b.activeSelf && Mathf.Abs(p.x - b.transform.position.x) < 2.6f && Mathf.Abs(p.y - b.transform.position.y) < 3.2f)
            {
                if (player.IsHeavy)
                    player.ShowMessage("Cracked wall: Jump + press S / Down Arrow in air to Heavy Slam", 0.5f);
                else
                    player.ShowMessage($"Cracked wall: need {Mathf.CeilToInt(player.heavySlamThreshold)}+ Light (Heavy) to Slam (S in air)", 0.5f);
            }
        }
    }

    static GameObject CloneGround(GameObject template, Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = Instantiate(template, pos, Quaternion.identity, parent);
        go.name = name;
        go.transform.localScale = scale;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = color;
        return go;
    }
}
