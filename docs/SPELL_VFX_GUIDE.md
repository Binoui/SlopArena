# Spell VFX Guide - Comment ajouter des effets visuels aux sorts

Guide pour ajouter des VFX aux abilities (fireball, lightning, ice, etc.)

## Approches

### 1. Sprite Sheets / Flipbook (Recommandé pour débuter)

**Avantages:**
- Facile à implémenter
- Beaucoup de ressources gratuites
- Performant
- Contrôle artistique précis

**Où trouver des sprites gratuits:**
- **OpenGameArt.org** - recherche "fire effect" ou "magic vfx"
- **itch.io/game-assets/free** - filter par "VFX"
- **Kenney.nl** - particle packs
- **CraftPix.net** - section free assets
- Google: "fire sprite sheet free", "magic vfx sprite sheet"

**Formats recherchés:**
- Sprite sheet (toutes les frames dans 1 image)
- Power of 2 dimensions (64x64, 128x128, 256x256)
- PNG avec transparence
- 4-16 frames d'animation

#### Exemple: Fireball Projectile avec Sprite Sheet

```
1. Télécharge un sprite sheet:
   fire_ball_sheet.png (512x64 = 8 frames de 64x64)

2. Structure Godot:
res://vfx/
├── textures/
│   └── fire_ball_sheet.png
└── FireballVFX.tscn
```

**FireballVFX.tscn:**
```
Node3D (root)
└─ Sprite3D
   ├─ Texture: fire_ball_sheet.png
   ├─ Hframes: 8  (horizontal frames)
   ├─ Vframes: 1  (vertical frames)
   └─ Billboard: Enabled
```

**Script d'animation:**

```csharp
public partial class FireballVFX : Sprite3D
{
    [Export] public int TotalFrames = 8;
    [Export] public float FPS = 15f; // Frames per second
    [Export] public bool Loop = true;

    private float _frameTimer = 0f;
    private int _currentFrame = 0;

    public override void _Process(double delta)
    {
        _frameTimer += (float)delta;

        float frameDuration = 1f / FPS;
        if (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrame++;

            if (_currentFrame >= TotalFrames)
            {
                if (Loop)
                    _currentFrame = 0;
                else
                    QueueFree(); // Destroy after animation
            }

            Frame = _currentFrame;
        }
    }
}
```

**Utilisation dans SpellResolver:**

```csharp
// Dans SpellResolver.cs ou StatusSpells.cs
private void SpawnFireballVFX(Vector3 position, Vector3 direction)
{
    var vfxScene = GD.Load<PackedScene>("res://vfx/FireballVFX.tscn");
    var vfx = vfxScene.Instantiate<FireballVFX>();
    
    _combat.AddToScene(vfx);
    vfx.GlobalPosition = position;
    
    // Orient vers la direction
    vfx.LookAt(position + direction, Vector3.Up);
}
```

---

### 2. GPU Particles avec Texture (Plus Dynamique)

**Avantages:**
- Effet plus vivant (particules individuelles)
- Facile de tweaker (couleur, vitesse, taille)
- Pas besoin de sprite sheet complexe
- Une seule texture suffit (rond/étoile)

**Textures nécessaires:**
- Un simple rond blanc (particle_round.png - 64x64)
- Ou une étoile (particle_star.png)
- Godot va colorier avec des gradients

#### Exemple: Flamme Continue (RMB charge)

**Créer la texture de base:**
```
1. Dans GIMP/Photoshop:
   - 64x64 canvas
   - Cercle blanc au centre
   - Gradient radial (center bright → edge transparent)
   - Export PNG avec alpha

2. Ou utilise celle-ci:
   res://vfx/textures/particle_round.png (fournie par Godot par défaut)
```

**Scene Setup:**
```
FlameEmitter.tscn
└─ GPUParticles3D
   ├─ Amount: 30
   ├─ Lifetime: 0.5
   ├─ Explosiveness: 0.0 (émission continue)
   ├─ Draw Pass 1: QuadMesh
   │   └─ Material: StandardMaterial3D
   │       ├─ Texture: particle_round.png
   │       ├─ Transparency: Alpha
   │       └─ Billboard: Enabled
   └─ Process Material: ParticleProcessMaterial
       ├─ Emission Shape: Box (0.2, 0.2, 0.2)
       ├─ Direction: (0, 1, 0) [upward]
       ├─ Spread: 20°
       ├─ Initial Velocity: 2-4
       ├─ Gravity: (0, 1, 0) [upward pour feu]
       ├─ Damping: 1.0
       ├─ Scale: 0.5 → 0.0 (curve)
       └─ Color Ramp:
           Start: (1.0, 0.8, 0.1) [yellow]
           Mid:   (1.0, 0.3, 0.0) [orange]
           End:   (0.2, 0.0, 0.0) [dark red]
```

**Script de contrôle:**

```csharp
public partial class FlameEmitter : GPUParticles3D
{
    public void StartFlame()
    {
        Emitting = true;
    }

    public void StopFlame()
    {
        Emitting = false;
        
        // Destroy après que toutes les particules finissent
        GetTree().CreateTimer(Lifetime).Timeout += () => QueueFree();
    }
}
```

**Utilisation:**

```csharp
// Dans le sort RMB charge (StatusSpells.cs)
private GPUParticles3D? _chargeFlameVFX;

private void StartChargeVFX(Vector3 position)
{
    var vfxScene = GD.Load<PackedScene>("res://vfx/FlameEmitter.tscn");
    _chargeFlameVFX = vfxScene.Instantiate<GPUParticles3D>();
    
    _combat.AddToScene(_chargeFlameVFX);
    _chargeFlameVFX.GlobalPosition = position;
    _chargeFlameVFX.Emitting = true;
}

private void StopChargeVFX()
{
    if (_chargeFlameVFX != null)
    {
        _chargeFlameVFX.Emitting = false;
        // Auto-destroy handled by particle lifetime
        _chargeFlameVFX = null;
    }
}
```

---

### 3. Shader Procedural (Pas besoin de texture!)

**Avantages:**
- Zéro fichier texture
- Totalement customizable
- Effet dynamique
- Petit file size

**Désavantages:**
- Plus complexe (GLSL)
- Moins de contrôle artistique précis

#### Exemple: Fireball Shader Simple

**fire_ball.gdshader:**
```glsl
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled;

uniform float time_scale : hint_range(0.0, 10.0) = 1.0;
uniform vec3 fire_color_hot : source_color = vec3(1.0, 0.9, 0.2);
uniform vec3 fire_color_cold : source_color = vec3(1.0, 0.2, 0.0);

// Noise function (Perlin-like)
float noise(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
    vec2 uv = UV * 2.0 - 1.0; // Center UV
    float dist = length(uv);
    
    // Animated noise
    float t = TIME * time_scale;
    float n = noise(uv * 3.0 + vec2(t, t * 0.5));
    
    // Fire shape (sphere with noise)
    float fire = smoothstep(1.0, 0.5, dist + n * 0.3);
    
    // Color gradient
    vec3 color = mix(fire_color_cold, fire_color_hot, n);
    
    ALBEDO = color;
    EMISSION = color * fire * 2.0; // Glow
    ALPHA = fire;
}
```

**Utilisation:**

```csharp
// Créer une sphere avec le shader
public partial class FireballShader : MeshInstance3D
{
    public override void _Ready()
    {
        // Mesh
        Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f };
        
        // Material avec shader
        var shader = GD.Load<Shader>("res://vfx/shaders/fire_ball.gdshader");
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("time_scale", 2.0f);
        MaterialOverride = mat;
    }
}
```

---

## 4. Asset Packs Recommandés (Gratuits)

### Pour Démarrer Rapidement

**Kenney Particle Pack:**
- https://kenney.nl/assets/particle-pack
- 100+ sprites (feu, fumée, magic, sparks)
- Style cartoon/stylisé
- PNG avec alpha

**Brackeys Assets (classic):**
- Recherche "Brackeys VFX pack" sur OpenGameArt
- Fire, lightning, ice effects
- Sprite sheets + individual frames

**Stylized VFX:**
- https://opengameart.org/content/magic-vfx-pack
- 50+ magic effects (fireball, ice, lightning)
- Multiple colors

---

## 5. Workflow Complet: Ajouter le Feu au RMB de Manki

### Étape par Étape

**1. Choisir l'approche:**
Pour un projectile de feu → **Sprite Sheet** OU **GPU Particles**

**2. Télécharger/Créer la texture:**
```
Exemple: fire_projectile_sheet.png (8 frames, 512x64)
Place dans: res://vfx/textures/
```

**3. Créer la scene VFX:**

**Option A: Sprite Sheet**
```
res://vfx/spells/FireProjectile.tscn
└─ Node3D (script: FireProjectileVFX.cs)
   └─ Sprite3D
      ├─ Texture: fire_projectile_sheet.png
      ├─ Hframes: 8
      ├─ Billboard: Enabled
      └─ Pixel Size: 0.01
```

**Option B: Particles**
```
res://vfx/spells/FireProjectile.tscn
└─ Node3D
   └─ GPUParticles3D
      ├─ Amount: 20
      ├─ Lifetime: 0.3
      ├─ Draw Pass: QuadMesh avec texture rond
      └─ Process Material: (config ci-dessus)
```

**4. Intégrer dans le sort:**

Trouve où le projectile RMB est créé (probablement dans `CharacterDefinition.cs` ou `StatusSpells.cs`):

```csharp
// Dans la fonction qui lance le projectile RMB
private void FireProjectile(Vector3 origin, Vector3 direction)
{
    // ... code existant du projectile ...
    
    // Ajouter le VFX
    var vfxScene = GD.Load<PackedScene>("res://vfx/spells/FireProjectile.tscn");
    var vfx = vfxScene.Instantiate<Node3D>();
    
    _combat.AddToScene(vfx);
    vfx.GlobalPosition = origin;
    vfx.LookAt(origin + direction, Vector3.Up);
    
    // Attacher au projectile ou détruire avec lui
    // Option 1: Parent au projectile (si tu as un projectile Node)
    // projectileNode.AddChild(vfx);
    
    // Option 2: Détruire après X secondes
    GetTree().CreateTimer(2.0f).Timeout += () => vfx.QueueFree();
}
```

**5. Tweaker:**
- Ajuste scale (`vfx.Scale = Vector3.One * 0.5f`)
- Ajuste vitesse d'animation (FPS dans script)
- Ajuste couleur (modulate ou shader parameters)

---

## 6. Quick Start: Fire VFX Sans Télécharger

Si tu veux tester sans chercher de texture:

**Utilise GPUParticles avec la texture par défaut de Godot:**

```csharp
public partial class QuickFireVFX : GPUParticles3D
{
    public override void _Ready()
    {
        // Setup programmatique (pas besoin de .tscn)
        Amount = 20;
        Lifetime = 0.5f;
        Explosiveness = 0.0f;
        
        // Mesh
        var quad = new QuadMesh { Size = new Vector2(0.2f, 0.2f) };
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1, 0.5f, 0.1f, 0.8f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
        };
        quad.Material = mat;
        DrawPass1 = quad;
        
        // Process material
        var process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        process.EmissionBoxExtents = new Vector3(0.1f, 0.1f, 0.1f);
        process.Direction = new Vector3(0, 1, 0);
        process.InitialVelocityMin = 2f;
        process.InitialVelocityMax = 4f;
        process.Gravity = new Vector3(0, 1, 0); // Upward (fire)
        
        // Color gradient
        var gradient = new Gradient();
        gradient.AddPoint(0.0f, new Color(1, 1, 0.3f)); // Yellow
        gradient.AddPoint(0.5f, new Color(1, 0.3f, 0)); // Orange
        gradient.AddPoint(1.0f, new Color(0.2f, 0, 0)); // Dark red
        process.ColorRamp = gradient;
        
        // Scale curve (shrink over time)
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 0.5f));
        curve.AddPoint(new Vector2(1, 0));
        process.ScaleCurve = curve;
        
        ProcessMaterial = process;
        
        Emitting = true;
    }
}
```

**Spawn:**
```csharp
var fire = new QuickFireVFX();
_combat.AddToScene(fire);
fire.GlobalPosition = position;

// Auto-destroy après 2s
GetTree().CreateTimer(2.0f).Timeout += () => fire.QueueFree();
```

---

## 7. Ma Recommandation pour Manki RMB

**Phase 1 (Placeholder - 10min):**
```csharp
// Juste une sphere orange qui glow
var sphere = new MeshInstance3D
{
    Mesh = new SphereMesh { Radius = 0.2f },
    MaterialOverride = new StandardMaterial3D
    {
        AlbedoColor = new Color(1, 0.5f, 0.1f),
        EmissionEnabled = true,
        Emission = new Color(1, 0.5f, 0),
        EmissionEnergyMultiplier = 3.0f
    }
};
```

**Phase 2 (Quick particles - 30min):**
Utilise `QuickFireVFX` (code ci-dessus) - zero assets needed

**Phase 3 (Polish - 1-2h):**
Télécharge un fire sprite sheet + fait une belle animation

**Commence par Phase 1 pour tester, upgrade plus tard!**

---

## 8. Ressources

**Textures gratuites:**
- https://opengameart.org/art-search?keys=fire
- https://itch.io/game-assets/free/tag-vfx
- https://kenney.nl/assets/particle-pack

**Tutoriels Godot VFX:**
- https://docs.godotengine.org/en/stable/tutorials/3d/particles/index.html
- Recherche YouTube: "Godot 4 fire effect tutorial"

**Outils:**
- **JuiceFX** (Godot addon) - VFX presets
- **Aseprite** - Créer tes propres sprite sheets
- **Particle Designer** - Visual particle editor

---

Tu veux que je te code un système de projectile avec VFX intégré pour Manki RMB? Ou tu préfères essayer d'abord avec le QuickFireVFX?
