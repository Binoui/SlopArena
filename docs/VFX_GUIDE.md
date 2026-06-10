# VFX Guide - Particles & Visual Effects

Guide rapide pour ajouter des effets visuels dans SlopArena.

## 1. Damage Numbers (✅ Implemented)

**Ce qui a été fait:**
- `DamageNumber.cs` - Label3D qui float up et fade
- `DamageNumberManager.cs` - Spawner central
- Connecté à `CombatComponent.OnTakeDamage` event

**Comment ça marche:**
```csharp
// Dans Main.cs, on subscribe à l'event:
combat.OnTakeDamage += (damage, kbX, kbY, kbZ) =>
{
    _damageNumbers.SpawnDamageNumber(damage, position);
};

// DamageNumber gère automatiquement:
// - Float up (RiseSpeed = 3 units/s)
// - Fade out (dernière 0.5s)
// - Color selon damage (blanc/jaune/rouge)
// - Size selon damage (0.008-0.016 pixel size)
// - Auto-destroy après 1.2s
```

**Personnalisation:**
- `TotalLifetime` - Durée de vie (actuellement 1.2s)
- `RiseSpeed` - Vitesse de montée (actuellement 3 units/s)
- `PixelSize` - Taille du texte selon damage
- `Modulate` - Couleur selon damage

---

## 2. Particles GPU (GPUParticles3D) - Recommandé

**Qu'est-ce que c'est:**
GPU particles = beaucoup de petites instances rendues par le GPU (très performant).

**Cas d'usage:**
- Hit sparks (100-200 particules explosant)
- Dash trail (émission continue pendant dash)
- Dust clouds à l'atterissage
- Shield bubble shimmer

### Exemple: Hit Sparks

```csharp
// 1. Créer une scene .tscn dans Godot Editor:
// HitSpark.tscn
// └─ GPUParticles3D
//    ├─ Amount: 50
//    ├─ Lifetime: 0.3
//    ├─ One Shot: true
//    ├─ Explosiveness: 1.0
//    └─ Process Material: ParticleProcessMaterial
//       ├─ Emission Shape: Sphere (radius 0.2)
//       ├─ Direction: Random
//       ├─ Initial Velocity: 5-8
//       ├─ Gravity: (0, -9.8, 0)
//       ├─ Damping: 2.0
//       ├─ Scale: 0.1 → 0.0 (curve)
//       └─ Color: Orange → Red (gradient)

// 2. Script pour spawner:
public partial class HitSparkPool : Node3D
{
    private PackedScene _sparkScene = null!;

    public override void _Ready()
    {
        _sparkScene = GD.Load<PackedScene>("res://vfx/HitSpark.tscn");
    }

    public void SpawnSpark(Vector3 position)
    {
        var spark = _sparkScene.Instantiate<GPUParticles3D>();
        AddChild(spark);
        spark.GlobalPosition = position;
        spark.Emitting = true;

        // Auto-destroy après lifetime
        GetTree().CreateTimer(spark.Lifetime + 0.1).Timeout += () => spark.QueueFree();
    }
}

// 3. Appeler depuis CombatComponent:
combat.OnTakeDamage += (damage, kbX, kbY, kbZ) =>
{
    _hitSparkPool.SpawnSpark(hitPosition);
};
```

### Propriétés Importantes

**GPUParticles3D:**
- `Amount` - Nombre de particules
- `Lifetime` - Durée de vie d'une particule
- `One Shot` - true = explosion, false = émission continue
- `Explosiveness` - 0-1 (0 = émission étalée, 1 = toutes en même temps)
- `Emitting` - Démarre/arrête l'émission

**ParticleProcessMaterial:**
- `Emission Shape` - Sphere/Box/Point pour spawn area
- `Direction` - Vecteur de départ
- `Initial Velocity` - Vitesse initiale (min/max)
- `Gravity` - Force de gravité (0, -9.8, 0) = réaliste
- `Damping` - Friction (0 = aucune, 5 = ralentit vite)
- `Scale Curve` - Taille over lifetime (0.5 → 0.0 = shrink)
- `Color Ramp` - Gradient de couleur over lifetime

---

## 3. Mesh Particles (MeshInstance3D + Shader) - Avancé

**Cas d'usage:**
- Dash trail (mesh qui suit le personnage)
- Shield bubble (sphere avec shader transparent)
- Charge-up aura

### Exemple: Dash Trail

```csharp
// 1. Créer un TrailMesh3D (Godot a un node dédié)
// Ou créer manuellement avec ImmediateMesh

public partial class DashTrail : MeshInstance3D
{
    private StandardMaterial3D _mat = null!;
    private float _lifetime = 0f;

    public override void _Ready()
    {
        // Créer un material transparent
        _mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.5f, 0.8f, 1.0f, 0.5f), // Cyan semi-transparent
            EmissionEnabled = true,
            Emission = new Color(0.5f, 0.8f, 1.0f),
            EmissionEnergyMultiplier = 2.0f
        };
        MaterialOverride = _mat;
    }

    public override void _Process(double delta)
    {
        _lifetime += (float)delta;

        // Fade out
        float alpha = 1f - (_lifetime / 0.5f);
        if (alpha < 0f)
        {
            QueueFree();
            return;
        }

        var color = _mat.AlbedoColor;
        _mat.AlbedoColor = new Color(color.R, color.G, color.B, alpha * 0.5f);
    }
}

// 2. Spawner dans DashState:
private void SpawnTrailSegment(Vector3 position)
{
    var trail = new DashTrail();
    GetTree().Root.AddChild(trail);
    trail.GlobalPosition = position;

    // Créer un simple quad mesh
    var mesh = new QuadMesh { Size = new Vector2(0.5f, 1.5f) };
    trail.Mesh = mesh;
}

// 3. Appeler chaque frame pendant le dash:
if (_dashTicks % 2 == 0) // Tous les 2 ticks = 30fps trail
    SpawnTrailSegment(_character.GlobalPosition);
```

---

## 4. Shaders (Pour Effets Avancés)

**Cas d'usage:**
- Shield bubble (fresnel effect)
- Hit flash (additive white overlay)
- Dissolve effect pour respawn

### Exemple: Hit Flash Shader

```glsl
// hit_flash.gdshader
shader_type spatial;

uniform float flash_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec3 flash_color : source_color = vec3(1.0, 1.0, 1.0);

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    ALBEDO = mix(tex.rgb, flash_color, flash_amount);
    EMISSION = flash_color * flash_amount * 2.0; // Boost emission
}
```

```csharp
// Usage dans CombatComponent:
private ShaderMaterial? _flashMaterial;
private float _flashTimer = 0f;

public void TakeDamage(float damage, float kbX, float kbY, kbZ)
{
    // ... existing code ...

    // Trigger flash
    _flashTimer = 0.15f; // 150ms flash
    if (_flashMaterial != null)
        _flashMaterial.SetShaderParameter("flash_amount", 1.0f);
}

public override void _Process(double delta)
{
    if (_flashTimer > 0f)
    {
        _flashTimer -= (float)delta;
        float flash = Mathf.Max(_flashTimer / 0.15f, 0f);
        _flashMaterial?.SetShaderParameter("flash_amount", flash);
    }
}
```

---

## 5. Object Pooling (Performance)

Quand tu spawn beaucoup de VFX (100+ hit sparks par seconde), utilise un object pool:

```csharp
public partial class VFXPool<T> : Node3D where T : Node3D, new()
{
    private Queue<T> _pool = new();
    private PackedScene? _scene;

    public void Initialize(PackedScene scene, int initialSize = 20)
    {
        _scene = scene;
        for (int i = 0; i < initialSize; i++)
        {
            var obj = scene.Instantiate<T>();
            obj.ProcessMode = ProcessModeEnum.Disabled; // Freeze
            AddChild(obj);
            _pool.Enqueue(obj);
        }
    }

    public T Spawn(Vector3 position)
    {
        T obj;
        if (_pool.Count > 0)
        {
            obj = _pool.Dequeue();
            obj.ProcessMode = ProcessModeEnum.Inherit;
        }
        else
        {
            // Pool vide, créer nouvelle instance
            obj = _scene!.Instantiate<T>();
            AddChild(obj);
        }

        obj.GlobalPosition = position;
        return obj;
    }

    public void Return(T obj)
    {
        obj.ProcessMode = ProcessModeEnum.Disabled;
        _pool.Enqueue(obj);
    }
}

// Usage:
private VFXPool<GPUParticles3D> _hitSparkPool = null!;

public override void _Ready()
{
    _hitSparkPool = new VFXPool<GPUParticles3D>();
    AddChild(_hitSparkPool);
    _hitSparkPool.Initialize(GD.Load<PackedScene>("res://vfx/HitSpark.tscn"), 30);
}

public void OnHit(Vector3 position)
{
    var spark = _hitSparkPool.Spawn(position);
    spark.Emitting = true;

    // Return après lifetime
    GetTree().CreateTimer(spark.Lifetime).Timeout += () => _hitSparkPool.Return(spark);
}
```

---

## 6. Roadmap VFX Recommandé

Par ordre de difficulté (facile → difficile):

### Facile (1-2h)
1. **Hit Sparks** - GPUParticles3D one-shot sur hit
2. **Landing Dust** - GPUParticles3D sur atterrissage (IsGrounded edge)
3. **Dash Particles** - GPUParticles3D trailing pendant dash

### Moyen (2-4h)
4. **Hit Flash Shader** - White flash sur damage
5. **Camera Shake** - SpringArm3D spring_length modulation
6. **Shield Bubble** - Sphere mesh + transparent shader

### Avancé (4-8h)
7. **Combo Trail** - Persistent trail mesh pendant combo
8. **Charge-up Aura** - Growing sphere particles pendant charge
9. **KO Explosion** - Big burst particles + screen flash

---

## 7. Tips Généraux

**Performance:**
- GPU Particles >> CPU Particles (10-100x faster)
- Object pooling pour spawns fréquents (>10/s)
- Limite Amount à <200 par emitter

**Visuals:**
- Toujours utiliser `Billboard = true` pour particles qui regardent la caméra
- `Emissive` materials pour glow effect (pas besoin de WorldEnvironment)
- Color gradients: Start bright → End dark (naturel fade out)

**Debug:**
- Activer "Visible Collision Shapes" dans Godot pour voir emission shapes
- `GD.Print($"Spawned VFX at {position}")` pour debug spawn location
- Frame profiler: Debug → Profiler → GPU pour voir particle cost

**Organisation:**
```
res://vfx/
├── particles/
│   ├── HitSpark.tscn
│   ├── DashTrail.tscn
│   └── LandingDust.tscn
├── shaders/
│   ├── hit_flash.gdshader
│   └── shield_bubble.gdshader
└── textures/
    ├── spark.png
    └── particle_round.png
```

---

## 8. Next Steps

Pour ton projet:

**Priorité 1 (impacts visuels immédiats):**
1. Hit sparks (orange particles sur hit)
2. Hit flash shader (white overlay 150ms)
3. Landing dust (petit puff quand tu atterris)

**Priorité 2 (polish):**
4. Dash trail (cyan particles trailing)
5. Camera shake sur big hits (>20% damage)
6. Shield visual si tu ajoutes un shield system

**Priorité 3 (avancé):**
7. Combo aura (glow qui s'intensifie pendant combo)
8. KO explosion (big burst quand blast zone)
9. Victory/respawn effects

Commence par Hit Sparks - c'est le plus facile et ça change immédiatement le feel du combat.
