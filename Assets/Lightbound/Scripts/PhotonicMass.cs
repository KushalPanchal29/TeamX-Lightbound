using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core "Body Made of Light (Photonic Mass)" twist controller:
/// - Scales the player's physical body and collider between Tiny (0.42x) and Heavy (1.18x) based on Light reserve.
/// - Adjusts movement speed and gravity dynamically with mass.
/// - Handles Ceiling Compression so the player never gets stuck inside narrow 0.6m crevices.
/// - Handles mid-air Heavy Slam (S / Down Arrow at 65+ Light) and Heavy Stomp on enemies.
/// </summary>
[RequireComponent(typeof(Player))]
public class PhotonicMass : MonoBehaviour
{
    [Header("Photonic Mass (Body Made of Light Twist)")]
    [Tooltip("Body scale at 0 light (Tiny: fits 0.6m gaps, low gravity, fast).")]
    public float minBodyScale = 0.42f;
    [Tooltip("Body scale at full 100 light (Heavy: stomps enemies, Heavy Slam).")]
    public float maxBodyScale = 1.18f;
    public float tinyMoveSpeed = 9.0f;
    public float heavyMoveSpeed = 6.8f;
    public float tinyGravityScale = 1.7f;
    public float heavyGravityScale = 2.95f;
    [Tooltip("Minimum light needed for Heavy abilities (Heavy Slam with S/Down & Enemy Stomp).")]
    public float heavySlamThreshold = 65f;
    public float slamVelocity = -22f;
    public float slamShockwaveRadius = 3.8f;

    public float CurrentScale { get; private set; } = 1f;
    public bool CeilingCompressed { get; private set; }
    public bool IsSlamming { get; set; }
    public bool IsHeavy => _player != null && _player.lightAmount >= heavySlamThreshold && !CeilingCompressed;
    public bool IsTiny => CurrentScale <= 0.65f;

    Player _player;
    Rigidbody2D _rb;
    BoxCollider2D _col;
    Transform _bodySprite;
    Vector2 _baseColSize = new Vector2(0.7f, 0.9f);
    Vector3 _baseSpriteScale = new Vector3(0.7f, 0.9f, 1f);
    Vector3 _baseVisorPos = new Vector3(0.15f, 0.2f, 0f);
    Vector3 _baseVisorScale = new Vector3(0.28f, 0.14f, 1f);

    public void Initialize(Player player, Rigidbody2D rb, BoxCollider2D col)
    {
        _player = player;
        _rb = rb;
        _col = col;
        if (_col != null) _baseColSize = _col.size;
        _bodySprite = transform.Find("Sprite");
        if (_bodySprite != null) _baseSpriteScale = _bodySprite.localScale;
        if (_player.visor != null)
        {
            _baseVisorPos = _player.visor.localPosition;
            _baseVisorScale = _player.visor.localScale;
        }

        CurrentScale = Mathf.Lerp(minBodyScale, maxBodyScale, Mathf.Clamp01(_player.lightAmount / _player.maxLight));
        if (_col != null) _col.size = _baseColSize * CurrentScale;
        ApplyVisualScale(_player.FacingDirection);
    }

    public float GetMoveSpeed(float lightRatio) => Mathf.Lerp(tinyMoveSpeed, heavyMoveSpeed, lightRatio);
    public float GetGravityScale(float lightRatio) => IsSlamming ? 6.5f : Mathf.Lerp(tinyGravityScale, heavyGravityScale, lightRatio);
    public float GetJumpMultiplier(float lightRatio) => Mathf.Lerp(1.06f, 1.0f, lightRatio);

    public void FixedUpdateScale(float lightRatio, int standOnMask, bool grounded)
    {
        float targetScale = Mathf.Lerp(minBodyScale, maxBodyScale, lightRatio);
        float nextScale = Mathf.MoveTowards(CurrentScale, targetScale, Time.fixedDeltaTime * 3.5f);

        float currentH = _baseColSize.y * CurrentScale;
        float nextH = _baseColSize.y * nextScale;
        Vector2 rbPos = _rb.position;
        float footY = rbPos.y - currentH * 0.5f;

        if (nextScale > CurrentScale)
        {
            float headTopY = footY + currentH;
            float proposedTopY = footY + nextH;
            float checkH = Mathf.Max(0.06f, proposedTopY - headTopY + 0.04f);
            Vector2 checkCenter = new Vector2(rbPos.x, headTopY + checkH * 0.5f);
            Vector2 checkSize = new Vector2(_baseColSize.x * nextScale * 0.8f, checkH);
            if (Physics2D.OverlapBox(checkCenter, checkSize, 0f, standOnMask) != null)
            {
                nextScale = CurrentScale;
                nextH = currentH;
                CeilingCompressed = true;
            }
            else
            {
                CeilingCompressed = false;
            }
        }
        else if (CeilingCompressed && targetScale > CurrentScale + 0.04f)
        {
            float headTopY = footY + currentH;
            Vector2 checkCenter = new Vector2(rbPos.x, headTopY + 0.08f);
            Vector2 checkSize = new Vector2(_baseColSize.x * CurrentScale * 0.8f, 0.12f);
            CeilingCompressed = Physics2D.OverlapBox(checkCenter, checkSize, 0f, standOnMask) != null;
        }
        else
        {
            CeilingCompressed = false;
        }

        if (Mathf.Abs(nextScale - CurrentScale) > 0.0001f)
        {
            if (grounded)
            {
                rbPos.y = footY + nextH * 0.5f;
                _rb.position = rbPos;
            }
            CurrentScale = nextScale;
            if (_col != null) _col.size = _baseColSize * CurrentScale;
        }
    }

    public void ApplyVisualScale(int facing)
    {
        if (_bodySprite != null)
        {
            _bodySprite.localScale = new Vector3(_baseSpriteScale.x * CurrentScale, _baseSpriteScale.y * CurrentScale, 1f);
        }
        if (_player != null && _player.visor != null)
        {
            _player.visor.localScale = new Vector3(_baseVisorScale.x * CurrentScale, _baseVisorScale.y * CurrentScale, 1f);
            _player.visor.localPosition = new Vector3(Mathf.Abs(_baseVisorPos.x) * CurrentScale * facing, _baseVisorPos.y * CurrentScale, 0f);
        }
    }

    public void ExecuteSlamShockwave(Vector2 slamPos, List<GameObject> crackedBlocks)
    {
        int broken = 0;
        if (crackedBlocks != null)
        {
            for (int i = crackedBlocks.Count - 1; i >= 0; i--)
            {
                var block = crackedBlocks[i];
                if (block == null || !block.activeSelf) continue;
                var bCol = block.GetComponent<Collider2D>();
                Vector2 closest = bCol != null ? bCol.ClosestPoint(slamPos) : (Vector2)block.transform.position;
                if (Vector2.Distance(slamPos, closest) <= slamShockwaveRadius)
                {
                    block.SetActive(false);
                    broken++;
                }
            }
        }

        int stunnedCount = 0;
        foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (Vector2.Distance(slamPos, enemy.transform.position) <= slamShockwaveRadius)
            {
                enemy.Stun(3.5f);
                stunnedCount++;
            }
        }

        if (_player != null)
        {
            if (broken > 0) _player.ShowMessage("Heavy Slam: broke cracked wall!");
            else if (stunnedCount > 0) _player.ShowMessage($"Heavy Slam: stunned {stunnedCount} enemy(s)!");
            else _player.ShowMessage("Heavy Slam!");
        }
    }
}
