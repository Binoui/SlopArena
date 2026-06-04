# Character Kit Design Principles

> Design patterns extraits de DKO, Battlerite, Supervive, Fangs, Omega Strikers, Smash.
> Ces patterns reviennent dans **tous** ces jeux — ce sont les fondamentaux d'un character kit
> pour un platform fighter 3D avec sorts.
>
> À utiliser comme base pour designer les kits des classes SlopArena.

---

## Les 8 Archétypes de Coups

Chaque sort/ability a un **job** précis. Un kit de personnage c'est une sélection
parmi ces archétypes. Personne n'a tout — chaque personnage a 4 à 6 slots,
répartis entre ces rôles.

### 1. Poke / Projectile
- Attaque à distance, skillshot directionnel
- Usage : spacing, zone control, chip damage
- CD : 0.5-3s (ou spamable avec dégâts faibles)
- Dans DKO : neutral-B, ou LMB (pour les mages)
- **Règle** : pas de homing. Le projectile va tout droit dans la direction du perso.

### 2. Mobility / Recovery
- Dash, téléport, sprint boost, grapple, leap
- Usage : fermer la distance (engage) ou sortir du danger (disengage)
- CD : 3-6s (court — le joueur doit pouvoir s'en servir souvent)
- **Pattern ultra fréquent : sur la touche E**
- Parfois double charge avec refire (Zeus Surge, DKO)

### 3. Crowd Control / Engage
- Stun, root, slow, silence, knockup, fear, pull
- Usage : **initier un combat** → toucher le CC → enchaîner
- CD : 6-10s
- **Pattern fréquent : sur la touche Q**
- Le CC en platform fighter dure 1-2s max. Plus = stunlock.

### 4. Zone / Area Denial
- AOE persistante, piège au sol, mur, nuage, flamme au sol
- Usage : contrôler un espace, forcer l'adversaire à bouger, couper une route
- CD : 8-12s
- Durée : 3-6s (assez pour être utile, pas assez pour être frustrant)
- Exemples : Ymir Ice Wall (6s), Sol Fireball burn ground, Thor Tectonic Rift (5s)

### 5. Counter / Parry
- "Trance" (Battlerite), "Protector Guardian" (Athena DKO)
- Usage : **read défensif** — si l'ennemi te tape pendant le buff → il est puni
- CD : 8-12s
- Fenêtre active : 0.5-0.75s
- **Règle** : 1 joueur par pool max, ou 1 counter très spécifique (ex: seulement contre les projectiles)
- Résultat : soit un stun pour toi, soit un gros dégât

### 6. Buff / Self-Enhancement
- Bouclier, damage boost, speed boost, invisibilité, résistance
- Usage : se préparer avant un engage, ou activer pendant un combat
- CD : 10-15s
- Durée : 4-8s
- **Règle** : le buff doit être visible par l'adversaire (feedback clair)

### 7. Combo Extender / Finisher
- Coup plus fort qui se place après un CC ou un setup
- Usage : **convertir un hit** en vrais dégâts — le "payoff"
- CD : 4-8s
- **Pattern commun** : conditionnel — "si target a slow → stun", "si target est stun → plus de dégâts"
- C'est l'archétype qui transforme le poke en kill threat

### 8. Ultimate / Burst
- Coup fort avec un impact visuel clair
- Usage : finisher, reversal, teamfight swing
- CD : 25-35s
- **Toujours sur R**
- **Pattern** : doit être dodgable mais impactful si ça touche
- Exemples : zone ult (DKO Aegis Charge), big damage ult (Battlerite), summon ult (Wukong clone)

---

## Les 4 Kits Archétypes

Dans DKO, Battlerite, et tous les jeux du genre, les personnages se répartissent
en 4 grands archétypes avec des sélections de sorts différentes :

### Rushdown / Brawler
| Slot | Rôle | CD |
|------|------|----|
| 1 / LMB | Poke faible ou light attack | 0s |
| Q | Mobility / engage | 4-6s |
| E | CC (knockup, stun court) | 6-8s |
| R | Big damage burst | 25s |
| Passif | Damage buff ou movespeed | permanent |

Exemples : Thor (DKO), Hercules (DKO), Shaggy (MultiVersus)
**Gameplan** : foncer → CC → damage. Pas de poke, pas de zone. Tout sur l'engage.

### Control / Zone
| Slot | Rôle | CD |
|------|------|----|
| 1 / LMB | Poke projectile | 1-2s |
| Q | CC (slow, root) | 7-10s |
| E | Zone / area denial | 8-12s |
| R | Big zone ult | 30s |
| Passif | Zone buff ou range bonus | permanent |

Exemples : Ymir (DKO), Sol (DKO), Iva (Battlerite)
**Gameplan** : piquer à distance → poser des zones → forcer l'adversaire dans un piège.

### Support / Utility
| Slot | Rôle | CD |
|------|------|----|
| 1 / LMB | Poke moyen | 1s |
| Q | Mobility (pour team) | 6-8s |
| E | Counter / Parry ou heal | 8-12s |
| R | Buff team / heal / res | 30s |
| Passif | Buff allié | permanent |

Exemples : Arthur (DKO), Poloma (Battlerite), Pearl (Omega Strikers)
**Gameplan** : rester en backline, buff les coéquipiers, counter les engages ennemis.

### Assassin / Glass Cannon
| Slot | Rôle | CD |
|------|------|----|
| 1 / LMB | Light attack rapide | 0s |
| Q | Mobility (invis, teleport) | 5-8s |
| E | Combo extender | 6-8s |
| R | Big single-target ult | 30s |
| Passif | Bonus damage sur condition | permanent |

Exemples : Loki (DKO), Izanami (DKO), Croak (Battlerite)
**Gameplan** : flanquer → burst → sortir. Risqué, récompense le skill individuel.

---

## Patterns de Touches

Basé sur la fréquence observée dans les 7 jeux analysés :

```
LMB / 1   → Attaque de base / Poke      (spamable, 0-1s CD)
RMB / 2   → Heavy / Combo extender      (3-6s CD)
Q         → CC / Engage                  (6-10s CD) ← pattern Q=CC
E         → Mobility / Recovery          (3-6s CD)  ← pattern E=recovery
R         → Ultimate                     (25-35s CD)
Shift     → Dodge physique               (1-3s CD, pas un sort)
F / 4     → Counter / Buff / Situational (8-12s CD)
```

### Notes sur les touches
- **E = recovery** c'est le pattern le plus solide — Battlerite, Fangs, DKO, Supervive
- **Q = CC** pareil — quasi unanime
- **R = ult** universel dans tous les jeux
- Les touches de sorts (Q, E, R) sont **reliables à la direction du perso**
  pour varier l'effet (optionnel, pas obligatoire)

---

## Règles de Design

### Règle 1 : Tout est skillshot
- Pas de homing, pas d'auto-aim
- La **direction du personnage** = direction du sort
- Le joueur vise avec son mouvement (ZQSD), pas avec un crosshair
- Un projectile va tout droit dans la direction où tu fais face au moment du cast

### Règle 2 : Les sorts ont un commit
- Startup + endlag. Plus le sort est fort, plus tu risques si tu whiff
- Légers : startup 0.1-0.2s, endlag 0.1s (tu peux bouger quasi tout de suite)
- Lourds : startup 0.3-0.6s, endlag 0.3s (tu es vulnérable si tu whiff)
- Ultimates : startup 0.5-1.0s (très visible, l'adversaire peut esquiver)

### Règle 3 : Le CC est la clé du combat
- Sans CC, personne ne peut punir → jeu de chip damage seulement
- Avec trop de CC, c'est du stunlock → pas fun
- **Sweet spot : 1 sort de CC par kit** (6-10s CD)
- Un CC dur (stun) dure 1-2s max en platform fighter

### Règle 4 : Un sort = un usage
- Chaque sort a **un job clair** : piquer, bouger, CC, zone, counter, buff, burst
- Pas de sorts "applique effet A + bonus si effet A déjà présent"
- Les synergies entre sorts de **slots différents** sont OK
  (ex : Q slow → E stun si slow) mais pas dans le même sort

### Règle 5 : Le recovery sort doit être utilisable souvent
- CD court (3-6s). C'est ce qui rend le combat dynamique
- Sans mobility, un perso est immobile et prévisible
- Le joueur doit pouvoir l'utiliser pour engage **et** disengage

### Règle 6 : Le counter/parry c'est optionnel mais fort
- Ça récompense la lecture, et ça fait un moment "outplay"
- 1 perso par pool max, ou réservé à un arché spécifique (support/control)
- Doit avoir un whiff punish (si le joueur rate son counter, il est vulnérable)

### Règle 7 : Pas de mana, que des cooldowns
- Les joueurs gèrent le **timing**, pas une ressource
- Les CD permettent de lire les patterns ennemis ("il a utilisé son Q, je peux engage")
- Exception possible : un perso avec self-damage (Thanatos) comme trade-off

### Règle 8 : Les sorts interagissent entre eux
- Pas d'effet imbriqué dans un seul sort
- Mais les **combinaisons entre sorts** sont encouragées
- Exemple DKO classique :
  1. Q = slow (CC)
  2. E = stun si target a slow (combo extender)
  3. R = gros dégâts pendant le stun (burst)

### Règle 9 : Chaque kit doit avoir une faiblesse claire
- Rushdown : faible à distance, predictable
- Control : faible en close range, vulnérable si rush
- Support : faible en 1v1, dépend des coéquipiers
- Assassin : fragile, meurt si le burst rate

### Règle 10 : Tous les personnages ont des attaques de base
- LMB = light attack (combo de 2-4 hits selon perso)
- RMB = heavy attack (plus lent, plus fort)
- Dans DKO les light attacks font 6/6/12 (total 24), les heavies font 10
- Même les mages ont des light attacks physiques (staff slap, etc.)

---

## Exemple d'Application pour SlopArena

Appliquons les patterns aux 3 classes existantes :

### Vanguard (Rushdown)
| Slot | Rôle | Typage | CD |
|------|------|--------|----|
| LMB | Light combo (3 hits) | Physique | 0s |
| RMB | Heavy smash | Physique | 0s, mais recovery |
| Q | CC engage (knockup) | Sort | 7s |
| E | Mobility dash | Sort | 4s |
| R | Big damage burst | Sort | 25s |

### Wraith (Assassin)
| Slot | Rôle | Typage | CD |
|------|------|--------|----|
| LMB | Light combo (rapide, 4 hits) | Physique | 0s |
| RMB | Heavy strike | Physique | 0s |
| Q | Invis / teleport | Sort | 8s |
| E | Combo extender (conditionnel) | Sort | 6s |
| R | Single-target burst | Sort | 30s |

### Channeler (Control)
| Slot | Rôle | Typage | CD |
|------|------|--------|----|
| LMB | Projectile poke | Sort | 1.5s |
| RMB | Heavy zone | Sort | 4s |
| Q | Slow / root | Sort | 8s |
| E | Zone area denial | Sort | 10s |
| R | Big zone ultimate | Sort | 30s |
| F | Counter / parry shield | Sort | 10s |

---

## Références

- `docs/research/dko-character-kits.md` — données brutes de DKO (13 dieux)
- Sessions de recherche : Battlerite (6 sorts, skillshots), Supervive (4 sorts + mouvement platform fighter), Fangs (2 sorts + ult), Rumble (mouvement pur)
