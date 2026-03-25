

# DEADSKY — THE KOVRAN THEATER
## Complementary Systems Addendum

---

```
 ██████╗  ███████╗  █████╗  ██████╗  ███████╗ ██╗  ██╗ ██╗   ██╗
 ██╔══██╗ ██╔════╝ ██╔══██╗ ██╔══██╗ ██╔════╝ ██║ ██╔╝ ╚██╗ ██╔╝
 ██║  ██║ █████╗   ███████║ ██║  ██║ ███████╗ █████╔╝   ╚████╔╝
 ██║  ██║ ██╔══╝   ██╔══██║ ██║  ██║ ╚════██║ ██╔═██╗    ╚██╔╝
 ██████╔╝ ███████╗ ██║  ██║ ██████╔╝ ███████║ ██║  ██╗    ██║
 ╚═════╝  ╚══════╝ ╚═╝  ╚═╝ ╚═════╝  ╚══════╝ ╚═╝  ╚═╝    ╚═╝
          ───── THE KOVRAN THEATER ─────
          Operation Pale Thunder
```

**The Setting:**
The **Kovran Peninsula** — a fictional volatile region where two coalitions clash. You command Battery ALPHA in **Sector 7** of the **Eastern Kovran Air Defense Network**, part of the **Allied Kovran Defense Force (AKDF)**. The enemy: the **Ravan Confederacy Air Force (RCAF)** — a well-equipped, tactically adaptive air force.

---

## A. ENEMY GROUP TACTICS & COMMANDER AI SYSTEM

### A.1 Enemy Force Structure — Hierarchical Command

```
RCAF STRIKE COMMAND (Theater Level — off-map, strategic AI)
│
├── WING COMMANDER (AI Agent — Primary Model)
│   │   Personality, risk tolerance, strategic thinking
│   │   Sees the big picture, allocates resources
│   │
│   ├── SQUADRON LEADER — "VENOM" Squadron (4-6 aircraft)
│   │   │   Tactical AI — leads formation, calls maneuvers
│   │   │   
│   │   ├── Element Lead (2-ship pair)
│   │   │   ├── Wingman 1
│   │   │   └── Wingman 2
│   │   │
│   │   └── Element Lead (2-ship pair)
│   │       ├── Wingman 3
│   │       └── Wingman 4
│   │
│   ├── SQUADRON LEADER — "SPECTER" Squadron (ECM/Escort)
│   │   └── ... 
│   │
│   └── SQUADRON LEADER — "HAMMER" Squadron (Reserves)
│       └── ...
│
└── INTELLIGENCE OFFICER (feeds info back to commander)
    Tracks which radars engaged, from where, what missiles used
```

### A.2 Enemy Commander Personalities (AI-Driven)

```csharp
public class EnemyCommanderProfile
{
    public string Name { get; set; }                    // "Colonel Dragan Kovac"
    public string Callsign { get; set; }                // "RAVEN ACTUAL"
    
    // Personality traits (0.0 - 1.0) — fed into AI system prompt
    public double Aggressiveness { get; set; }          // 0.8 = pushes hard
    public double Caution { get; set; }                 // 0.3 = willing to take risks
    public double Adaptability { get; set; }            // 0.9 = quickly changes tactics
    public double Creativity { get; set; }              // 0.7 = uses unconventional approaches
    public double LoyaltyToMission { get; set; }        // 0.6 = may abort if losses mount
    public double ToleranceForLosses { get; set; }      // 0.4 = pulls back after 40% losses
    public double RespectForEnemy { get; set; }         // grows over time if you perform well
    
    // Learning state — persists across missions
    public List<string> KnownADPositions { get; set; }  // Learned from losses
    public List<string> KnownRadarTypes { get; set; }   // Identified by RWR
    public List<string> TacticsTriedAndFailed { get; set; }
    public List<string> TacticsTriedAndSucceeded { get; set; }
    public int TotalAircraftLostToYou { get; set; }
    public int SuccessfulStrikesAgainstYou { get; set; }
}
```

**Commanders rotate/escalate:**
```
Mission 1-3:   Captain Yusuf Maric  — Cautious, probing, learning
Mission 4-7:   Major Dragan Kovac   — Aggressive, adaptive, experienced  
Mission 8-10:  Colonel Sera Volin   — Brilliant tactician, unpredictable
Mission 11+:   General Arkan Drost  — Theater commander personally directing,
                                       uses everything, no mercy
```

### A.3 Group Tactics & Formations (Algorithm-Driven + AI-Modified)

```
COORDINATED TACTICS THE ENEMY CAN USE:

1. PINCER ATTACK
   ┌──→ Group A (4 aircraft, bearing 030°)
   │                                          ← Converge on target
   └──→ Group B (4 aircraft, bearing 330°)
   
   Forces you to split radar attention. AI decides timing to synchronize arrival.

2. FEINT & STRIKE
   Group A (2 fighters) — flies aggressive, triggers your radar, draws fire
   Group B (4 strikers) — sneaks in low from different axis during engagement
   
   AI evaluates: "Did the enemy radar lock Group A? Good — send Group B NOW"

3. ECM ESCORT PACKAGE
   ┌── ECM Aircraft (EA-6B type) — stands off at 60nm, jams your radar
   │   Creates noise sector on your display
   │
   └── Strike package flies THROUGH the jammed sector
       You can't see them clearly until they emerge at close range

4. SWARM / SATURATION
   8-12 aircraft from multiple bearings simultaneously
   Overwhelm your engagement capacity (you only have 4 launchers)
   AI calculates: "He has 4 launchers, 3 ready. Send 6+ simultaneous targets"

5. POP-UP ATTACK
   Aircraft fly at 200ft (below radar horizon)
   Pop up to 5000ft at 15nm to deliver weapons
   Then dive back down
   You get ~30 seconds of detection window

6. TERRAIN MASKING
   AI uses terrain elevation data to route aircraft behind hills/mountains
   Aircraft appear and disappear from radar as terrain blocks line of sight

7. DRONE DECOY
   Send cheap drones first to trigger SAM launches
   Waste your expensive missiles on $5000 drones
   Then send real strikers when you're reloading

8. SEAD (Suppression of Enemy Air Defense)
   Dedicated aircraft carry anti-radiation missiles
   When your radar emits — they detect it and fire at YOUR position
   Forces you to turn radar off or get hit
   Creates terrifying "MISSILE INBOUND" moments

9. TIME-ON-TARGET
   Multiple groups timed to arrive at your engagement zone simultaneously
   Even from different distances (far group launches first, close group waits)

10. RETREAT AND RETURN
    Attack, take fire, retreat convincingly
    You relax, maybe start reloading
    They turn around and come back at full speed
```

### A.4 Group Coordination Algorithm

```csharp
public class FormationManager
{
    // Each formation maintains relative positions
    public enum Formation
    {
        LineAbreast,        // Side by side — max firepower forward
        Echelon,            // Stepped diagonal — mutual support
        Trail,              // Single file — minimum radar cross section
        Spread,             // Wide spacing — hard to engage multiple
        Wedge,              // V-shape — balanced offense/defense
        WallOfEagles,       // Wide line — maximum coverage
        StackedHigh,        // Multiple altitudes — vertical separation
    }

    // Wingman behavior
    public enum WingmanRole
    {
        Escort,             // Stay with lead, mutual support
        Sweep,              // Fly ahead, detect threats first
        Decoy,              // Deliberately draw fire
        ECMSupport,         // Provide electronic cover
        BombDamageAssessment, // Follow up to confirm strike results
    }

    // Group-level decision making (runs in simulation, informed by AI)
    public void UpdateFormation(AircraftGroup group, double deltaTime)
    {
        // Leader makes decisions for the group
        // Wingmen follow formation rules with slight individual variation
        // If leader is killed → next in rank takes over (brief confusion period)
        // If group takes >50% casualties → AI decides break/retreat
        // Damaged aircraft may fall behind formation
    }
}
```

### A.5 Enemy Intelligence & Learning

```csharp
public class EnemyIntelligence
{
    // The enemy LEARNS about you across engagements
    
    public List<RadarEmission> DetectedEmissions { get; set; }
    // When your radar is on, enemy RWR detects bearing to you
    // After 2-3 detections from different angles → they triangulate your position
    
    public Dictionary<string, int> EngagementRecords { get; set; }
    // "SAM launched from bearing 225° at range 15nm" → builds engagement envelope knowledge
    
    public List<string> LessonsLearned { get; set; }
    // "Low altitude approach from south was not detected until 8nm"
    // "SAM has minimum range — aircraft that got within 2nm were safe"
    // "Radar turned off after we fired ARM — he's scared of SEAD"
    
    // Fed into enemy commander AI prompt:
    // "Based on previous missions, you've learned: {lessons_learned}"
    // This makes each mission harder and more interesting
}
```

---

## B. ECONOMY & SHOP SYSTEM

### B.1 Currency: **Operational Budget (OB)**

```
You don't "earn coins" — you receive OPERATIONAL BUDGET allocations.
Budget comes from:
  - Mission completion bonuses
  - Performance multipliers (efficiency, low ammo waste)
  - Medals/commendations (bonus allocations from high command)
  - Intel contributions (identifying new threats)
  - Protecting high-value targets successfully
  
You SPEND budget on:
  - Equipment upgrades
  - Ammunition resupply
  - Personnel recruitment
  - Training programs
  - Maintenance & repairs
  - Field modifications
```

### B.2 The Requisition Terminal (Shop UI)

```
┌────────────── REQUISITION TERMINAL ──────────────────────┐
│                                                           │
│  OPERATIONAL BUDGET: ◈ 45,200 OB                         │
│  PENDING DELIVERIES: 2                                    │
│  NEXT RESUPPLY WINDOW: Mission 7                          │
│                                                           │
│  ┌── CATEGORIES ─────────────────────────────────────┐   │
│  │                                                    │   │
│  │  [RADAR SYSTEMS]  [MISSILES]  [LAUNCHERS]         │   │
│  │  [ELECTRONICS]    [PERSONNEL] [TRAINING]          │   │
│  │  [FIELD MODS]     [SUPPORT]   [INTELLIGENCE]      │   │
│  │                                                    │   │
│  └────────────────────────────────────────────────────┘   │
│                                                           │
│  ┌── RADAR SYSTEMS ──────────────────────────────────┐   │
│  │                                                    │   │
│  │  CURRENT: P-18 "Spoon Rest" (Search Radar)        │   │
│  │  Range: 80nm | Min Alt: 500ft | ECM Resist: LOW   │   │
│  │                                                    │   │
│  │  ► UPGRADE: P-37 "Bar Lock"           ◈ 12,000    │   │
│  │    Range: 120nm | Min Alt: 300ft | ECM: MEDIUM     │   │
│  │    Delivery: 1 mission | Install: during mission   │   │
│  │    ⚠ Radar offline for 5 min during installation  │   │
│  │                                                    │   │
│  │  ► UPGRADE: 96L6 "Cheese Board"       ◈ 28,000    │   │
│  │    Range: 180nm | Min Alt: 100ft | ECM: HIGH       │   │
│  │    REQUIRES: Rank Captain or higher                │   │
│  │    REQUIRES: 3 successful intercepts with P-37     │   │
│  │    🔒 LOCKED                                       │   │
│  │                                                    │   │
│  │  ► ADDON: Low-Altitude Detection Module ◈ 5,000   │   │
│  │    Improves low-alt detection by 40%               │   │
│  │    Compatible with any radar                       │   │
│  │                                                    │   │
│  │  ► ADDON: ECCM Suite                   ◈ 8,000    │   │
│  │    Reduces jamming effectiveness by 50%            │   │
│  │    Compatible with P-37 and above                  │   │
│  │                                                    │   │
│  └────────────────────────────────────────────────────┘   │
│                                                           │
│  NOTE: Deliveries arrive between missions.               │
│  Combat-damaged equipment costs 50% to repair.            │
│  Some items require rank or merit prerequisites.          │
│                                                           │
└───────────────────────────────────────────────────────────┘
```

### B.3 Complete Shop Categories

```csharp
public static class ShopCatalog
{
    // ═══════════════════════════════════════
    // RADAR SYSTEMS (Base upgrades)
    // ═══════════════════════════════════════
    
    // Tier 1 (Starting)
    "P-18 Spoon Rest"       // Range 80nm, basic, ECM vulnerable
    
    // Tier 2 (◈ 12,000)
    "P-37 Bar Lock"         // Range 120nm, better low-alt, moderate ECM resistance
    
    // Tier 3 (◈ 28,000) [Requires: Captain rank + 3 intercepts with T2]
    "96L6 Cheese Board"     // Range 180nm, excellent low-alt, high ECM resistance
    
    // Tier 4 (◈ 55,000) [Requires: Major rank + 15 total kills + specific merit]
    "Nebo-M Composite"      // Range 240nm, VHF band (can detect stealth), full ECCM
    
    // Radar add-ons:
    "Low-Altitude Module"           // ◈ 5,000 — improves low-alt detection
    "ECCM Processing Suite"         // ◈ 8,000 — reduces jamming effectiveness
    "IFF Upgrade Module"            // ◈ 3,000 — faster/more reliable IFF
    "Track-While-Scan Processor"    // ◈ 6,000 — more simultaneous tracks (8→16→24)
    "Passive Detection Antenna"     // ◈ 10,000 — detect aircraft by their emissions (no radar needed)
    "Data Link Terminal"            // ◈ 7,000 — receive tracks from other radars (networked defense)
    
    // ═══════════════════════════════════════
    // MISSILE SYSTEMS
    // ═══════════════════════════════════════
    
    // Tier 1 (Starting)
    "9M38 Gadfly (SA-11)"          // Max range 18nm, Pk 0.70, semi-active guidance
    
    // Tier 2 (◈ 15,000) [Requires: 5 kills]
    "9M317 Grizzly (SA-17)"        // Max range 30nm, Pk 0.80, better seeker
    
    // Tier 3 (◈ 35,000) [Requires: Lt rank + 12 kills]
    "48N6 (SA-10 Grumble)"         // Max range 80nm, Pk 0.85, powerful, high altitude
    
    // Tier 4 (◈ 70,000) [Requires: Colonel rank + special achievement]
    "40N6 (SA-21 Growler)"         // Max range 200nm, Pk 0.90, can engage stealth
    
    // Specialty missiles:
    "Proximity Frag Warhead Upgrade" // ◈ 4,000 — +5% Pk
    "Infrared Terminal Seeker"       // ◈ 6,000 — missile goes active terminal, fire-and-forget
    "Extended Range Motor"           // ◈ 5,000 — +20% range
    "Anti-ARM Decoy Missile"         // ◈ 8,000 — fire to lure enemy ARMs away from you
    
    // Ammunition resupply (per missile):
    "Tier 1 missile resupply"        // ◈ 800 each
    "Tier 2 missile resupply"        // ◈ 1,500 each
    "Tier 3 missile resupply"        // ◈ 3,000 each
    "Tier 4 missile resupply"        // ◈ 6,000 each
    
    // ═══════════════════════════════════════
    // LAUNCHERS
    // ═══════════════════════════════════════
    
    "Additional Launcher Bay"        // ◈ 10,000 — 4→5→6 launchers (max 8)
    "Rapid Reload Mechanism"         // ◈ 7,000 — 50% faster reload time
    "Auto-Loader System"             // ◈ 12,000 — reload doesn't require crew, automatic
    "Hardened Launch Position"        // ◈ 8,000 — reduces damage from enemy strikes
    "Mobile Launcher Upgrade"         // ◈ 15,000 — can relocate battery (takes 10 min in-game)
    
    // ═══════════════════════════════════════
    // ELECTRONICS & SYSTEMS
    // ═══════════════════════════════════════
    
    "Backup Power Generator"          // ◈ 4,000 — prevents total power failure
    "Hardened Communications"          // ◈ 5,000 — comms resistant to jamming
    "Encrypted Data Link"              // ◈ 6,000 — enemy can't intercept your comms
    "Automated Threat Evaluation"      // ◈ 8,000 — AI-assisted threat prioritization
    "Battle Damage Assessment Cam"     // ◈ 3,000 — visual confirmation of kills
    "Decoy Emitter"                    // ◈ 9,000 — makes enemy think your radar is elsewhere
    "Warning Receiver Upgrade"         // ◈ 5,000 — detect incoming ARMs earlier
    
    // ═══════════════════════════════════════
    // PERSONNEL
    // ═══════════════════════════════════════
    
    "Recruit Replacement Soldier"      // ◈ 1,000 — green, needs training
    "Request Experienced Transfer"     // ◈ 3,000 — comes with some skill
    "Request Specialist"               // ◈ 5,000 — expert in one area
    "Field Medic Attachment"           // ◈ 4,000 — wounded crew recovers faster
    "Morale Officer"                   // ◈ 2,000 — passive morale boost for all crew
    
    // ═══════════════════════════════════════
    // TRAINING PROGRAMS
    // ═══════════════════════════════════════
    
    "Basic Proficiency Course"         // ◈ 1,500 — all crew +5% base skill over 2 missions
    "Advanced Radar Operations"        // ◈ 3,000 — radar operators get faster track init
    "Missile Engagement Drill"         // ◈ 3,000 — +3% Pk from crew skill factor
    "ECM Recognition Training"        // ◈ 2,500 — crew better at identifying jamming
    "Stress Inoculation Training"      // ◈ 4,000 — crew more resistant to panic
    "Emergency Procedures Drill"       // ◈ 2,000 — faster response to damage/emergencies
    
    // ═══════════════════════════════════════
    // FIELD MODIFICATIONS
    // ═══════════════════════════════════════
    
    "Camouflage Netting"              // ◈ 1,500 — enemy has harder time finding you visually
    "Sandbag Reinforcement"           // ◈ 1,000 — personnel survive attacks better
    "Dispersal Plan"                   // ◈ 3,000 — spread equipment = less damage per hit
    "Backup Antenna"                   // ◈ 2,000 — if primary antenna hit, quick swap
    "Field Repair Kit (Advanced)"      // ◈ 3,500 — repair damaged systems faster in-mission
    
    // ═══════════════════════════════════════
    // SUPPORT ASSETS (call-in during mission)
    // ═══════════════════════════════════════
    
    "Fighter CAP Contract (1 mission)"  // ◈ 5,000 — 2 allied fighters on station
    "AWACS Support (1 mission)"         // ◈ 8,000 — extended radar coverage from airborne radar
    "Artillery Support Package"         // ◈ 4,000 — can call artillery on downed pilot positions
    "Search & Rescue Helicopter"        // ◈ 3,000 — recover downed allied pilots
    "Resupply Convoy (mid-mission)"     // ◈ 6,000 — emergency missile resupply during battle
    
    // ═══════════════════════════════════════
    // INTELLIGENCE
    // ═══════════════════════════════════════
    
    "SIGINT Intercept Station"          // ◈ 7,000 — occasionally intercept enemy comms
    "Satellite Reconnaissance Pass"     // ◈ 4,000 — pre-mission intel on enemy staging
    "Double Agent Intel Package"        // ◈ 10,000 — know enemy attack plan in advance
    "Radar Warning Network Sub"         // ◈ 3,000 — early warning from border stations
}
```

### B.4 Delivery & Installation Rules

```
DELIVERY TIMING:
├── Small items (add-ons, ammo, personnel)  → Available next mission
├── Medium items (radar upgrades, launchers) → 1-2 missions delivery time
├── Large items (tier 3-4 systems)           → 2-3 missions delivery time
├── Support contracts                        → Immediate (next mission)
└── Emergency field delivery (2x cost)       → Available mid-mission (risky convoy)

INSTALLATION:
├── Add-ons           → Instant (between missions)
├── Radar upgrades    → 5-10 minutes in-game (RADAR OFFLINE during install!)
│                       Choose when to install — during lull or risk it during action
├── Launcher additions → Between missions only
├── Training          → Gradual effect over 2-3 missions
└── Field mods        → Between missions
```

---

## C. PROGRESSION & RANK SYSTEM

### C.1 Player Rank Progression

```
RANK              REQUIRED         UNLOCK
────────────────────────────────────────────────────────────────
2nd Lieutenant    Starting rank    Basic equipment, 4 launchers
1st Lieutenant    ◈ 5,000 earned   Tier 2 radar available
                  + 3 missions     
                  + 2 kills

Captain           ◈ 20,000 earned  Tier 3 radar, 6 launcher slots
                  + 8 missions     Can request fighter support
                  + 10 kills       Second radio channel access
                  + 1 merit

Major             ◈ 50,000 earned  Tier 3 missiles, 8 launcher slots
                  + 15 missions    Can command allied batteries
                  + 25 kills       Full channel access
                  + 3 merits       Advanced scenarios unlock

Lieutenant Colonel ◈ 100,000 earned Tier 4 equipment available
                   + 25 missions    Direct line to CASTLE (national cmd)
                   + 50 kills       Can request strategic assets
                   + 5 merits
                   + 0 friendly fire incidents

Colonel           ◈ 200,000 earned  Everything unlocked
                  + 40 missions     Design own scenarios (editor)
                  + 100 kills       Prestige equipment variants
                  + "Hero of Kovran" medal

Brigadier General  Special achievement  ??? (hidden rank)
                   Win final campaign mission with 0 losses
```

### C.2 Merit / Commendation System

```csharp
public class MeritSystem
{
    // Merits are special achievements — harder than just kills
    // Each merit grants: rank progress + budget bonus + crew morale boost
    
    public static List<Merit> AllMerits = new()
    {
        // ═══ COMBAT MERITS ═══
        new Merit("First Blood", 
            "Score your first kill",
            bonus: 1000, oneTime: true),
            
        new Merit("Double Tap", 
            "Destroy 2 aircraft in under 30 seconds",
            bonus: 2000, oneTime: false),
            
        new Merit("Perfect Salvo", 
            "Hit 4 consecutive targets without a miss",
            bonus: 5000, oneTime: false),
            
        new Merit("Ace", 
            "Destroy 5 aircraft in a single mission",
            bonus: 8000, oneTime: false),
        
        new Merit("Untouchable", 
            "Complete a mission with all launchers still operational",
            bonus: 3000, oneTime: false),
            
        new Merit("Against All Odds", 
            "Survive and win against 3:1 numerical odds",
            bonus: 10000, oneTime: true),
            
        new Merit("Steel Nerves", 
            "Engage a target within 3nm (danger close)",
            bonus: 4000, oneTime: true),
            
        new Merit("Ghost", 
            "Win a mission using passive radar only (no emissions)",
            bonus: 15000, oneTime: true),
            
        // ═══ EFFICIENCY MERITS ═══
        new Merit("Economical", 
            "Complete mission using ≤50% of available missiles",
            bonus: 3000, oneTime: false),
            
        new Merit("Budget Hawk", 
            "Accumulate ◈50,000 in savings",
            bonus: 5000, oneTime: true),
            
        new Merit("Sharpshooter", 
            "Maintain >80% hit rate over 5 missions",
            bonus: 7000, oneTime: true),
            
        // ═══ LEADERSHIP MERITS ═══
        new Merit("Beloved Commander", 
            "All crew members at EXCELLENT morale simultaneously",
            bonus: 4000, oneTime: true),
            
        new Merit("No One Left Behind", 
            "Complete 10 missions with zero crew casualties",
            bonus: 8000, oneTime: true),
            
        new Merit("Mentor", 
            "Train 3 soldiers to maximum proficiency",
            bonus: 5000, oneTime: true),
            
        // ═══ SPECIAL MERITS ═══
        new Merit("Cold War", 
            "Make an enemy flight abort without firing a shot (they detect your radar and turn away)",
            bonus: 6000, oneTime: true),
            
        new Merit("Intercepted", 
            "Successfully contact and warn off an unknown aircraft on GUARD frequency",
            bonus: 3000, oneTime: false),
            
        new Merit("Hero of Kovran", 
            "Win the final campaign mission — requires all other combat merits",
            bonus: 50000, oneTime: true),
    };
}
```

### C.3 Soldier / Crew System — Individual Personnel

```csharp
public class Soldier
{
    // ═══ IDENTITY ═══
    public string FirstName { get; set; }           // "Aleksei"
    public string LastName { get; set; }            // "Petrov"
    public string Nickname { get; set; }            // "Steady" (earned over time)
    public string Rank { get; set; }                // "Corporal"
    public int Age { get; set; }                    // 23
    public string Background { get; set; }          // "Former mechanic from Kovran City"
    
    // ═══ ROLE ═══
    public CrewRole Role { get; set; }              // RadarOperator, LauncherCrew, CommsOfficer, etc.
    public int AssignedStation { get; set; }         // Which launcher/station
    
    // ═══ SKILLS (0.0 - 1.0, improve with experience) ═══
    public double Proficiency { get; set; }          // Overall job competence
    public double ReactionTime { get; set; }         // How fast they respond to events
    public double StressResistance { get; set; }     // How well they perform under fire
    public double TechnicalSkill { get; set; }       // Repair ability, system knowledge
    public double Communication { get; set; }        // Radio clarity, reporting accuracy
    
    // ═══ STATE (changes in real-time during missions) ═══
    public double Morale { get; set; }               // 0.0 = broken, 1.0 = sky-high
    public double Fatigue { get; set; }              // Increases over long missions
    public double Fear { get; set; }                 // Spikes when under attack
    public double Confidence { get; set; }           // Grows with successful engagements
    public HealthStatus Health { get; set; }         // Healthy, Lightly Wounded, Wounded, Critical, KIA
    
    // ═══ EXPERIENCE ═══
    public int MissionsServed { get; set; }
    public int KillsContributed { get; set; }
    public int TimesUnderFire { get; set; }
    public List<string> MeritsEarned { get; set; }   // Individual soldier merits
    public double ExperiencePoints { get; set; }     // Used for skill improvement
    
    // ═══ PERSONALITY (affects AI-generated radio messages) ═══
    public PersonalityType Personality { get; set; }  // Calm, Nervous, Aggressive, Joker, Professional
    public string SpeechPattern { get; set; }         // AI prompt hint: "speaks tersely" / "talks too much"
    
    // ═══ RELATIONSHIPS ═══
    public Dictionary<string, double> Relationships { get; set; }
    // Soldiers develop bonds — losing a close friend devastates morale
    // "Petrov and Marchuk: 0.9 (best friends)" → Marchuk KIA → Petrov morale crashes
}

public enum CrewRole
{
    BatteryCommander,       // You — but also your AI representation for crew interactions
    RadarOperator,          // Operates the radar — skill affects detection quality
    TrackingSupervisor,     // Manages tracks — skill affects track accuracy
    WeaponsOfficer,         // Controls engagement — skill affects Pk bonus
    LauncherCrew1,          // Operates launcher 1 — skill affects reload speed
    LauncherCrew2,          // Operates launcher 2
    LauncherCrew3,          // Operates launcher 3
    LauncherCrew4,          // Operates launcher 4
    CommunicationsOfficer,  // Handles radio — skill affects message clarity
    MaintenanceTech,        // Repairs damage — skill affects repair speed
    SentryObserver,         // Visual lookout — can spot low-flying aircraft visually
}

public enum PersonalityType
{
    SteadyProfessional,     // "Contact bearing 045, range 30, angels 18. Tracking."
    NervousRookie,          // "Uh— sir? I think I see— yeah, there's something at 045..."
    AggressiveVeteran,      // "Got the bastard on scope! 045, 30 miles. Let's light him up!"
    CalmVeteran,            // "New contact. 045 for 30. No rush, he's still far out."
    GrimRealist,            // "Another one. 045, 30 miles. Here we go again."
    Joker,                  // "We've got company — 045, 30 out. Hope he brought snacks."
}
```

### C.4 Soldier Rank Progression

```
ENLISTED RANKS:
Private (E-1)       → Starting for new recruits
PFC (E-2)           → After 2 missions survived
Corporal (E-3)      → After 5 missions + demonstrated skill
Sergeant (E-4)      → After 10 missions + leadership moments
Staff Sergeant (E-5) → After 15 missions + individual merit
Master Sergeant (E-6) → After 25 missions + exceptional record

EFFECTS OF SOLDIER RANK:
├── Higher rank = better base skill modifier
├── Higher rank = more resilient morale
├── Higher rank = better performance under stress
├── Higher rank = positive influence on nearby lower-rank soldiers
├── Higher rank soldiers get upset if commanded poorly (morale penalty)
└── Losing a high-rank soldier is a bigger morale hit to the unit
```

### C.5 How Crew Affects Gameplay (REAL IMPACT)

```csharp
public class CrewPerformanceModifiers
{
    // Radar operator skill affects:
    // - Time to establish track (skilled = faster, unskilled = slower/miss some)
    // - Track accuracy (position jitter reduced with skill)
    // - Ability to detect low-RCS targets (skill adds virtual sensitivity)
    // - Identification speed (IFF response time)
    // - UNDER FEAR: may report false contacts, lose tracks, freeze up
    
    // Weapons officer skill affects:
    // - Pk modifier (+/- up to 10% based on skill)
    // - Ability to handle multiple simultaneous engagements
    // - Time to calculate firing solution
    // - Judgment on "should we shoot?" ambiguous situations
    // - UNDER FEAR: may hesitate to launch, or launch at friendlies
    
    // Launcher crew skill affects:
    // - Reload time (base * (2.0 - skill)) → skilled = faster reload
    // - Misfire probability (unskilled = rare but possible missile failures)
    // - Maintenance state of launcher (skill prevents degradation)
    // - UNDER FEAR: fumble reload (restart timer), or abandon post
    
    // Comms officer skill affects:
    // - Message clarity and completeness
    // - Speed of relaying orders
    // - Ability to filter important from routine traffic
    // - UNDER FEAR: garbled messages, missed transmissions
    
    // Maintenance tech skill affects:
    // - Repair speed (base * (2.0 - skill))
    // - Can field-repair damaged radar (if skilled enough)
    // - Prevents random equipment degradation
    // - UNDER FEAR: makes mistakes, possibly makes damage worse
}
```

---

## D. REAL-TIME EVENTS SYSTEM — THE LIVING WORLD

### D.1 Event Categories

```csharp
public enum EventCategory
{
    // ═══ COMBAT EVENTS (triggered by gameplay) ═══
    CombatEngagement,       // Standard — missiles, kills, misses
    EnemySEAD,              // Anti-radiation missile incoming at YOUR position
    FriendlyFire,           // You accidentally engaged a friendly aircraft
    NearMiss,               // Missile passed within 100m but didn't kill
    AircraftCrash,          // Enemy aircraft crashes (not your kill — mechanical)
    PilotBailout,           // Enemy pilot ejects — rescue/capture decision
    CollateralDamage,       // Missile debris damages civilian area
    
    // ═══ EQUIPMENT EVENTS (random, probability-based) ═══
    RadarMalfunction,       // Radar glitches — temporarily degrades
    LauncherJam,            // Launcher mechanism jams — crew must fix
    PowerFluctuation,       // Generator hiccup — brief brownout
    CoolingFailure,         // Radar overheats — must reduce power or shut down
    CommsInterference,      // Radio static increases — hard to communicate
    FalseAlarm,             // Radar ghost contact — crew reports bogey that isn't there
    AntennaDamage,          // Weather or debris damages antenna — reduced performance
    SoftwareGlitch,         // Track computer crashes — restart needed (30 seconds)
    AmmoDefect,             // Missile fails pre-launch check — must be removed (1 launcher down)
    
    // ═══ PERSONNEL EVENTS (character-driven) ═══
    CrewPanic,              // Soldier panics under fire — temporarily useless
    CrewHeroism,            // Soldier does something brave — morale boost to all
    CrewArgument,           // Two soldiers argue — both perform worse temporarily
    CrewBreakdown,          // Soldier has emotional breakdown after trauma
    CrewBonding,            // Two soldiers bond during downtime — relationship improves
    CrewSick,               // Soldier falls ill — degraded performance or absent
    CrewInsubordination,    // Soldier refuses order (if morale very low)
    CrewSuggestion,         // Experienced soldier suggests tactic (AI generated, actually useful)
    CrewPrayer,             // Soldier quietly prays before engagement — affects mood
    CrewJoke,               // Someone cracks a joke — brief morale boost
    CrewNightmare,          // Soldier reports nightmares — long-term stress indicator
    CrewLetter,             // Soldier received letter from home — mood change
    CrewBirthday,           // It's someone's birthday — small morale event
    CrewPTSD,               // After many engagements — soldier develops PTSD symptoms
    
    // ═══ STRATEGIC EVENTS (higher HQ, world events) ═══
    ROEChange,              // Rules of engagement changed from above
    ReinforcementsArriving, // Allied reinforcements inbound
    AlliedBatteryDestroyed, // Nearby friendly battery got hit — you're more exposed
    AlliedBatteryOnline,    // New allied battery comes online
    IntelUpdate,            // Intelligence reports enemy staging activity
    CeasefireOrder,         // Temporary ceasefire ordered — MUST stop shooting
    CeasefireViolation,     // Enemy violates ceasefire — ROE confusion
    HighValueTarget,        // VIP aircraft detected — special orders to protect/destroy
    NuclearAlert,           // Strategic threat level escalation
    DiplomaticIncident,     // Accidental engagement of civilian/neutral aircraft
    SupplyConvoyAmbushed,   // Your resupply was attacked — delivery delayed
    MediaAttention,         // News crew wants to cover your battery — pressure to perform
    PresidentialVisit,      // VIP visiting nearby — heightened security posture
    
    // ═══ ENVIRONMENTAL EVENTS ═══
    WeatherChange,          // Weather shifts — affects visibility, radar, flight
    Thunderstorm,           // Heavy rain — radar clutter, reduced detection
    Fog,                    // Thick fog — visual observation impossible
    Sandstorm,              // Sandstorm — equipment degradation, visibility zero
    WindShift,              // Strong winds — affects missile trajectory slightly
    SunGlare,               // Setting/rising sun — visual observation difficult
    NightFall,              // Transition to night operations — different challenges
    
    // ═══ CIVILIAN / MORAL DILEMMA EVENTS ═══
    CivilianAirliner,       // Civilian aircraft entering your sector — DO NOT SHOOT
    MedicalEvacFlight,      // Medical helicopter in area — protected by convention
    RefugeeFlight,          // Aircraft carrying refugees from conflict zone
    UnidentifiedSlowMover,  // Slow aircraft — could be civilian or drone
    EnemyUsesHumanShield,   // Enemy flies near civilian aircraft deliberately
    DowedPilotRescue,       // Allied pilot down in area — rescue forces inbound
    
    // ═══ ESPIONAGE / INTELLIGENCE EVENTS ═══
    EnemyCommsIntercept,    // You intercept fragment of enemy radio
    DoubleAgentTip,         // Agent provides enemy attack plan (may be real or trap)
    RadarSignatureAnalysis, // Intel identifies new aircraft type from your radar data
    EnemySpyDetected,       // Counter-intel reports enemy knows your exact position
    Disinformation,         // Fake intel designed to trick you (AI plays tricks)
}
```

### D.2 Event Probability & Trigger System

```csharp
public class EventEngine
{
    // Events are triggered by:
    // 1. TIMERS — random intervals with probability
    // 2. THRESHOLDS — when game state reaches certain conditions
    // 3. AI DECISIONS — AI agents create events organically
    // 4. CHAINS — one event triggers another (cascading)
    
    private List<EventRule> _rules = new()
    {
        // Equipment events — random per mission
        new EventRule
        {
            Event = EventCategory.RadarMalfunction,
            Trigger = TriggerType.Random,
            BaseProbabilityPerMinute = 0.005, // ~0.5% per minute
            ModifiedBy = (state) => {
                double modifier = 1.0;
                modifier *= (2.0 - state.MaintenanceTech.Proficiency); // bad tech = more failures
                modifier *= state.RadarRunTime > 60 ? 1.5 : 1.0;      // long runtime = more likely
                modifier *= state.Weather.Precipitation > 0 ? 1.3 : 1.0; // rain = more issues
                if (state.HasUpgrade("BackupPowerGenerator")) modifier *= 0.5; // upgrade helps
                return modifier;
            }
        },
        
        // Crew panic — triggered by conditions
        new EventRule
        {
            Event = EventCategory.CrewPanic,
            Trigger = TriggerType.Threshold,
            Condition = (state) => {
                return state.Soldiers.Any(s => 
                    s.Fear > 0.85 && 
                    s.StressResistance < 0.4 && 
                    s.Health == HealthStatus.Healthy);
                // High fear + low stress resistance = panic candidate
            },
            CooldownSeconds = 120 // Don't spam this event
        },
        
        // Crew heroism — triggered when someone SHOULD panic but doesn't
        new EventRule
        {
            Event = EventCategory.CrewHeroism,
            Trigger = TriggerType.Threshold,
            Condition = (state) => {
                return state.Soldiers.Any(s =>
                    s.Fear > 0.9 &&
                    s.StressResistance > 0.8 &&
                    s.Confidence > 0.7);
            }
        },
        
        // Crew jokes — more likely during calm moments
        new EventRule
        {
            Event = EventCategory.CrewJoke,
            Trigger = TriggerType.Random,
            BaseProbabilityPerMinute = 0.02,
            ModifiedBy = (state) => {
                if (state.AlertLevel >= AlertLevel.Red) return 0.0; // no jokes under fire
                if (state.AverageMorale > 0.7) return 1.5;          // good mood = more jokes
                return 1.0;
            }
        },
        
        // Civilian airliner — strategic event
        new EventRule
        {
            Event = EventCategory.CivilianAirliner,
            Trigger = TriggerType.Scripted, // scenario defines when
            // Spawns a large, slow-moving radar contact
            // IFF responds as CIVILIAN
            // HQ radio: "ALPHA, be advised civilian traffic in sector. DO NOT ENGAGE."
            // If player shoots it down → game-changing consequences
            // Enemy might time their attack to coincide with civilian traffic
        },
        
        // Enemy spy detected — chain event
        new EventRule
        {
            Event = EventCategory.EnemySpyDetected,
            Trigger = TriggerType.Chain,
            ChainedFrom = "enemy_commander_successful_intel_gathering",
            // After enemy successfully identifies your position from RWR data
            // Intel tells you: "Counter-intelligence reports your position is compromised"
            // Next mission: enemy knows exactly where you are
            // Upgrade opportunity: buy "Mobile Launcher Upgrade" to relocate
        }
    };
}
```

### D.3 Real-Time Mood & Atmosphere System

```csharp
public class AtmosphereEngine
{
    // The FEELING of the game changes based on cumulative state
    
    public AtmosphereState CurrentAtmosphere { get; private set; }
    
    public void Update(GameState state)
    {
        // ═══ TENSION LEVEL (0.0 - 1.0) ═══
        double tension = 0.0;
        tension += state.HostileContacts.Count * 0.1;           // More contacts = more tension
        tension += state.MissilesInFlight * 0.15;                // Missiles flying = high tension
        tension += state.IncomingThreats * 0.3;                  // Something coming at YOU
        tension += (1.0 - state.AverageMorale) * 0.2;           // Low morale = tense
        tension += state.RecentDeaths * 0.25;                    // Recent casualties
        tension -= state.RecentKills * 0.05;                     // Kills relieve some tension
        tension = Math.Clamp(tension, 0.0, 1.0);
        
        // Tension affects:
        // - Audio: ambient hum pitch, radio static level, music intensity
        // - Visuals: subtle screen shake at high tension, warning light flash rate
        // - AI messages: crew speaks faster, shorter sentences under tension
        //                crew speaks casually when relaxed
        // - Event probabilities: panic more likely at high tension
        //                        jokes more likely at low tension
        
        // ═══ TIME PRESSURE (mission clock) ═══
        // Early mission: briefing mode, calm setup
        // Mid mission: contacts appearing, escalating
        // Late mission: desperate if losing, triumphant if winning
        // Post-mission: debrief, see results, crew reactions
        
        // ═══ AMBIENT STATE ═══
        CurrentAtmosphere = new AtmosphereState
        {
            Tension = tension,
            TimeOfDay = state.TimeOfDay,         // Dawn/day/dusk/night affects lighting
            Weather = state.Weather,              // Storm = sounds of rain, thunder
            AlertLevel = state.AlertLevel,        // Colors, alarm state
            CrewMood = state.AverageMorale,       // Background chatter frequency
            RecentEventIntensity = state.RecentEventIntensity
        };
    }
}
```

### D.4 Event Examples — How They Play Out

```
═══════════════════════════════════════════════════════════════
EVENT: CREW PANIC
═══════════════════════════════════════════════════════════════

Trigger: Cpl. Yuri Novak (Launcher 2 crew) has:
  Fear: 0.92 (near enemy strike just landed close)
  Stress Resistance: 0.3 (rookie)
  Missions: 2 (very inexperienced)

What happens:
1. RADIO (Battery Net): 
   "[garbled] I CAN'T—  I can't do this! They're going to 
    hit us! We need to go! WE NEED TO GO!" — Novak

2. GAMEPLAY EFFECT:
   Launcher 2 goes OFFLINE — Novak has abandoned his post
   
3. CREW RESPONSE (AI-generated, varies by crew personality):
   Sgt. Marchuk (Steady Professional): 
   "Novak! Get back to your station! That's an ORDER!"
   
   Pvt. Dima (Nervous Rookie): 
   "Oh god... is he right? Are they targeting us?"
   → Dima's fear increases slightly

4. PLAYER CHOICE:
   You can type a message on Battery Net:
   - Reassuring: "All positions hold. We're safe. Novak, return to station."
     → Roll against your leadership + Novak's remaining composure
     → If succeed: Novak returns (shaken but functional) in 30-60 seconds
   - Harsh: "Novak, if you don't get back on that launcher RIGHT NOW—"
     → Faster return but Novak's morale tanks, relationship damaged
   - Ignore: Let crew handle it
     → Marchuk handles it eventually (takes longer)
     → Respect for you drops slightly

5. AFTERMATH:
   If Novak returns: Launcher 2 back online, but Novak has -20% performance for rest of mission
   If Novak doesn't return: Launcher 2 offline, may need reassignment
   Either way: Event logged, affects future behavior
   Novak gets "Shell Shocked" trait → needs "Stress Inoculation Training" or may get worse

═══════════════════════════════════════════════════════════════
EVENT: CIVILIAN AIRLINER  
═══════════════════════════════════════════════════════════════

Trigger: Scripted at 12 minutes into "Dawn Patrol" scenario

What happens:
1. NEW RADAR CONTACT appears: 
   Large RCS, slow (250kts), high altitude (FL350)
   Bearing 090°, range 95nm, heading 270° (crossing your sector)
   
2. IFF: Responds — CIVILIAN (Mode 3 squawk 7700... wait, that's emergency)

3. RADIO (Command Net):
   ECHO ACTUAL: "ALPHA, be advised Kovran Air civilian traffic inbound 
   your sector. Kovran Air Flight 217, Airbus A320. 142 souls on board. 
   UNDER NO CIRCUMSTANCES ENGAGE. Acknowledge."

4. SIMULTANEOUSLY (AI decides to exploit this):
   Enemy commander thinks: "SAM battery will be distracted by civilian 
   traffic. Route strike package on similar bearing — they won't shoot 
   near the airliner."
   
   Enemy aircraft spawn at bearing 085° — VERY close to the civilian track.

5. YOUR RADAR shows two contacts very close together — one is the airliner,
   the others are fighters using it as cover.

6. TENSION: 
   - Crew: "Sir, I have military contacts near the civilian! They're 
     using the airliner as cover!"
   - HQ: "ALPHA, WEAPONS HOLD on bearing 085 to 095 until civilian is clear."
   - Enemies are getting closer, hiding behind the airliner's track.
   - If you shoot and accidentally hit the airliner → GAME OVER level consequence
   - If you wait too long, enemy strikers get through

7. PLAYER MUST:
   - Wait for separation between civilian and military contacts
   - OR use STT to precisely track just the military contacts
   - OR contact enemies on GUARD: "Unknown aircraft bearing 087, 
     you are entering restricted airspace. Identify yourself."
   - OR request fighter intercept (if purchased) to visually ID and separate

═══════════════════════════════════════════════════════════════
EVENT: CREW LETTER FROM HOME
═══════════════════════════════════════════════════════════════

Trigger: Random, between-mission event

What happens:
1. NOTIFICATION (between missions):
   "Sgt. Marchuk received a letter from his wife in Kovran City.
    She reports their apartment building was damaged by shelling.
    Family is safe but relocated to a shelter."

2. EFFECT:
   Marchuk: Morale -15%, but Motivation +20% (now fighting for his family)
   Marchuk's next AI-generated messages may reference this:
   "Let's make sure those bastards don't get through. My family's 
    counting on us."

3. CREW REACTION:
   Other soldiers: slight morale change (empathy, fear for own families)
   If you address it: "Marchuk, we'll keep your family safe. That's 
   why we're here." → Marchuk loyalty and morale boost

═══════════════════════════════════════════════════════════════
EVENT: RADAR MALFUNCTION DURING ENGAGEMENT
═══════════════════════════════════════════════════════════════

Trigger: Random, during active tracking

What happens:
1. RADAR DISPLAY: Suddenly fills with static/noise for 2 seconds
   Contacts become inaccurate or disappear
   Audio: alarming electronic buzzing sound

2. RADIO (Battery Net):
   Radar Operator: "SIR! Radar malfunction! I'm getting garbage data! 
   Standby — running diagnostics!"
   
3. GAMEPLAY:
   - Radar display shows degraded data for 15-30 seconds
   - Existing tracks coast (predicted positions only, accuracy degrades)
   - No new detections possible
   - Any missiles in flight lose guidance → go ballistic (will miss)
   - Maintenance tech works on it (speed based on their skill)

4. IF YOU HAVE "Backup Power Generator" upgrade:
   "Switching to backup power — radar should stabilize momentarily."
   Recovery in 5-10 seconds instead of 15-30.

5. CREW REACTIONS:
   Nervous soldiers: fear spikes
   Veteran soldiers: "Again? Get it fixed, we're blind out here."
   
6. ENEMY AI NOTICES:
   If enemy has detected your radar was on and it suddenly went off:
   "Enemy SAM radar went silent! NOW! All aircraft — accelerate ingress!"
   
═══════════════════════════════════════════════════════════════
EVENT: ENEMY PILOT BAILOUT / CAPTURE
═══════════════════════════════════════════════════════════════

Trigger: After destroying an aircraft

What happens:
1. AI-generated event (not every kill — probability based on aircraft type,
   altitude at destruction, etc.)

2. RADIO (Intel Net):
   "ALPHA, intel reports enemy pilot ejected from destroyed aircraft at 
   coordinates N34.12 E35.68. Ground forces moving to capture."

3. OPTIONAL: Contact on GUARD frequency:
   Enemy pilot: "Mayday, mayday. [callsign] Venom 2-1, I am down. 
   Request rescue. Position..."
   
   You could respond (opens diplomatic/psychological gameplay):
   "Venom 2-1, this is AKDF Battery ALPHA. Remain where you are. 
    You will be treated according to convention."

4. ENEMY COMMANDER REACTION (AI):
   If enemy hears their pilot's mayday → may send rescue helicopter
   → new radar contact (helicopter, slow, low)
   → do you let the rescue helicopter through? Shoot it? It's unarmed...
   → Moral dilemma + crew reactions

5. IF CAPTURED:
   Intel officer may extract useful information after a few missions:
   "Captured RCAF pilot confirms next strike package will use 
    terrain-following approach from the southwest."
   → Actual gameplay-useful intel

═══════════════════════════════════════════════════════════════
EVENT: VETERAN SOLDIER SUGGESTION
═══════════════════════════════════════════════════════════════

Trigger: Experienced soldier (15+ missions) notices tactical opportunity

What happens:
1. RADIO (Battery Net):
   MSgt. Volkov: "Commander, I've been watching that contact at 
   bearing 090. He's flying a racetrack pattern — I think he's 
   a recon bird mapping our radar. If we go silent for 2 minutes 
   then light him up at minimum range, we might bag him before 
   his friends know what happened."

2. This is actually valid tactical advice — the AI generated it based on
   the actual game state. The suggestion is real and would work.

3. PLAYER CHOICE:
   - Follow the advice → good outcome likely, Volkov morale+, respect+
   - Ignore → no penalty, but Volkov slightly disappointed
   - Modify: "Good thinking Volkov, but let's wait until he's in 
     the dead zone at 040° where the hills block his wingman's RWR."
     → Best outcome: Volkov impressed, better suggestion next time
```

---

## E. EVOLUTION OVER TIME — CAMPAIGN PROGRESSION

### E.1 Campaign Structure

```
THE KOVRAN CAMPAIGN — Operation Pale Thunder

CHAPTER 1: "First Contact" (Missions 1-3)
├── Difficulty: Easy
├── Enemy: Small probing flights, tentative
├── Your battery: Basic equipment, green crew
├── Purpose: Learn the game mechanics
├── Events: Tutorial-style, gentle introduction
└── Enemy commander: Cpt. Maric (cautious, probing)

CHAPTER 2: "Escalation" (Missions 4-7)
├── Difficulty: Medium
├── Enemy: Coordinated 2-ship and 4-ship attacks
├── Upgrades available: Tier 2 radar, better missiles
├── Events: First SEAD threats, first ECM encounters
├── Crew: Starting to differentiate — some good, some struggling
└── Enemy commander: Maj. Kovac (aggressive, learning from Ch.1)

CHAPTER 3: "The Storm" (Missions 8-12)  
├── Difficulty: Hard
├── Enemy: Full packages — fighters, bombers, ECM, SEAD
├── Upgrades: Tier 3 available for high performers
├── Events: Civilian dilemmas, crew PTSD, equipment strain
├── Crew: Veterans emerging, bonds formed, losses hurt more
├── Allied battery may be destroyed → you cover their sector too
└── Enemy commander: Col. Volin (brilliant, unpredictable)

CHAPTER 4: "Darkest Hour" (Missions 13-16)
├── Difficulty: Very Hard
├── Enemy: Mass raids, stealth drones, cruise missiles, everything
├── Events: Political pressure, ceasefire negotiations (may break)
├── Crew: Hardened or broken — depending on your leadership
├── Major moral dilemmas with consequences
└── Enemy commander: Gen. Drost (theater commander, ruthless)

CHAPTER 5: "Pale Thunder" (Missions 17-20)
├── Difficulty: Extreme
├── Final push — enemy throws everything
├── Your crew either at peak or barely holding together
├── Equipment either top-tier or patched together
├── Every decision matters
├── Final mission: defend against all-out assault
└── Outcome depends on EVERYTHING: equipment, crew, skill, choices
```

### E.2 Between-Mission Flow

```
┌────────────── BETWEEN MISSIONS ─────────────────────────┐
│                                                          │
│  ┌── MISSION DEBRIEF ────────────────────────────────┐  │
│  │                                                    │  │
│  │  Mission 4: "Kovac's Gambit"                      │  │
│  │  Result: VICTORY ✓                                 │  │
│  │                                                    │  │
│  │  Contacts Detected:    8                           │  │
│  │  Engagements:          5                           │  │
│  │  Missiles Fired:       7                           │  │
│  │  Confirmed Kills:      4                           │  │
│  │  Probable Kills:       1                           │  │
│  │  Misses:               2                           │  │
│  │  Hit Rate:             71.4%                       │  │
│  │                                                    │  │
│  │  Enemy Aircraft Leaked: 0 (PERFECT DEFENSE)        │  │
│  │  Crew Casualties:      0                           │  │
│  │  Equipment Damage:     Minor (radar antenna)       │  │
│  │                                                    │  │
│  │  BUDGET EARNED:  ◈ 8,500                           │  │
│  │    Base reward:     ◈ 5,000                        │  │
│  │    Kill bonus:      ◈ 2,000  (4 × 500)           │  │
│  │    Efficiency:      ◈ 1,000  (>70% hit rate)      │  │
│  │    Perfect defense:  ◈ 500                         │  │
│  │                                                    │  │
│  │  MERITS EARNED:                                    │  │
│  │    ★ "Double Tap" — 2 kills within 30 seconds     │  │
│  │                                                    │  │
│  │  [CONTINUE]                                        │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌── CREW STATUS ────────────────────────────────────┐  │
│  │                                                    │  │
│  │  Sgt. Marchuk  — Morale: ██████████ 92% (High)   │  │
│  │    Promoted! Corporal → Sergeant                   │  │
│  │    Skill improved: Proficiency 0.65 → 0.70        │  │
│  │                                                    │  │
│  │  Cpl. Novak    — Morale: ██████░░░░ 55% (Low)    │  │
│  │    Status: "Shaken after near-miss incident"       │  │
│  │    ⚠ Recommend: Stress Inoculation Training       │  │
│  │                                                    │  │
│  │  Pvt. Dima     — Morale: ████████░░ 78% (Good)   │  │
│  │    Gained experience: 2 missions → 3 missions      │  │
│  │    Bond formed with Marchuk (shared foxhole moment)│  │
│  │                                                    │  │
│  │  [VIEW ALL CREW]  [ASSIGN ROLES]  [TRAINING]      │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌── REQUISITION TERMINAL ───────────────────────────┐  │
│  │  (Shop — see section B)                            │  │
│  │  Budget: ◈ 45,200                                  │  │
│  │  [OPEN REQUISITION TERMINAL]                       │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌── INTEL BRIEFING ─────────────────────────────────┐  │
│  │                                                    │  │
│  │  NEXT MISSION: "Fog of War"                        │  │
│  │  Expected threat: Medium-High                      │  │
│  │  Weather: Overcast, light rain, visibility 5nm     │  │
│  │  Intel: "RCAF observed staging MiG-29s at Ravan    │  │
│  │          Air Base. Possible fighter sweep expected  │  │
│  │          from NE sector. SEAD capability unknown."  │  │
│  │                                                    │  │
│  │  Enemy Commander: Major Dragan Kovac               │  │
│  │  Assessment: "Aggressive. Lost 4 aircraft last     │  │
│  │              mission. May change tactics."          │  │
│  │                                                    │  │
│  │  [BEGIN MISSION]                                    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

### E.3 What Evolves Over Time

```
EVOLVES VIA MONEY (SHOP):
├── Radar capability (range, sensitivity, ECM resistance)
├── Missile types (range, Pk, guidance modes)
├── Number of launchers (4 → 8)
├── Support systems (power, cooling, comms)
├── Support assets (fighters, AWACS contracts)
├── Intelligence capabilities
└── Field modifications (survivability)

EVOLVES VIA TIME / EXPERIENCE:
├── Soldier skills (proficiency, reaction, stress resistance)
├── Soldier rank (E-1 through E-6)
├── Soldier relationships (bonds, rivalries)
├── Soldier personalities (grow, change based on events)
├── Your reputation (how crew sees you)
├── Battery efficiency (overall unit performance metric)
└── Muscle memory (crew gets faster at routine tasks)

EVOLVES VIA PLAYER RANK:
├── Equipment unlocks (higher tier gear requires rank)
├── Command authority (who you can order, what you can request)
├── Channel access (more communication options)
├── Scenario access (harder scenarios unlock with rank)
├── Allied asset availability
└── Strategic decision influence

EVOLVES VIA MERITS:
├── Special unlock prerequisites (certain gear needs specific merits)
├── Crew morale boosts (merits inspire the crew)
├── Budget bonuses (merits come with allocation increases)
├── Reputation with high command (more trust = more freedom)
└── Hidden unlocks (certain merit combinations unlock secrets)

EVOLVES VIA ENEMY LEARNING:
├── Enemy tactics get smarter (learns from your patterns)
├── Enemy commanders escalate (better leaders assigned)
├── Enemy equipment escalates (newer aircraft, ECM, ARMs)
├── Enemy intelligence improves (knows more about you)
└── Enemy adapts: if you always go silent, they bring radar-seeking missiles
                  if you always shoot at max range, they fly lower
                  if you ignore drones, they flood you with drones
```

---

## F. UPDATED PROJECT STRUCTURE (NEW FILES)

```
src/
├── IronDome.Core/  →  DEADSKY.Core/
│   ├── ... (all existing files renamed)
│   │
│   ├── Economy/                            ★ NEW
│   │   ├── Budget.cs                       # Player's operational budget
│   │   ├── ShopCatalog.cs                  # All purchasable items
│   │   ├── ShopItem.cs                     # Individual item definition
│   │   ├── UpgradeTree.cs                  # Tier dependencies
│   │   ├── PurchaseManager.cs              # Buy, deliver, install logic
│   │   ├── DeliveryScheduler.cs            # When purchases arrive
│   │   └── MissionRewardCalculator.cs      # Post-mission budget calculation
│   │
│   ├── Personnel/                          ★ NEW
│   │   ├── Soldier.cs                      # Full soldier model (see C.3)
│   │   ├── CrewRoster.cs                   # All current soldiers
│   │   ├── SkillSystem.cs                  # Skill improvement over time
│   │   ├── MoraleEngine.cs                 # Morale calculation, effects
│   │   ├── FearSystem.cs                   # Real-time fear responses
│   │   ├── RelationshipTracker.cs          # Soldier bonds/rivalries
│   │   ├── PersonalitySystem.cs            # Personality traits, speech patterns
│   │   ├── InjurySystem.cs                 # Wounds, recovery, KIA
│   │   ├── RankProgression.cs              # Soldier promotions
│   │   ├── TrainingProgram.cs              # Training system
│   │   ├── SoldierNameGenerator.cs         # Realistic name generation
│   │   └── CrewPerformanceModifiers.cs     # How crew affects gameplay
│   │
│   ├── Progression/                        ★ NEW
│   │   ├── PlayerRank.cs                   # Player rank system
│   │   ├── MeritSystem.cs                  # Achievements/commendations
│   │   ├── UnlockManager.cs                # What's locked/unlocked
│   │   ├── CampaignProgress.cs             # Campaign state
│   │   └── StatisticsTracker.cs            # Lifetime stats
│   │
│   ├── Events/                             ★ NEW
│   │   ├── EventEngine.cs                  # Event triggering system
│   │   ├── EventDefinition.cs              # Event definitions
│   │   ├── EventRule.cs                    # Trigger rules/probabilities
│   │   ├── EventChainManager.cs            # Cascading events
│   │   ├── AtmosphereEngine.cs             # Tension/mood calculation
│   │   └── EventOutcomeProcessor.cs        # Process event consequences
│   │
│   ├── EnemyAI/                            ★ NEW
│   │   ├── EnemyCommanderProfile.cs        # Commander personality
│   │   ├── EnemyIntelligence.cs            # What enemy knows about you
│   │   ├── FormationManager.cs             # Group formations/coordination
│   │   ├── TacticLibrary.cs                # Catalog of available tactics
│   │   ├── GroupCoordinator.cs             # Multi-group synchronization
│   │   ├── SquadronStructure.cs            # Wing/squadron/element hierarchy
│   │   └── EnemyLearningSystem.cs          # Cross-mission adaptation
│   │
│   └── Campaign/                           ★ NEW
│       ├── CampaignDefinition.cs           # Chapter/mission structure
│       ├── CampaignManager.cs              # Manage campaign flow
│       ├── MissionTransition.cs            # Between-mission phase
│       ├── MissionDebrief.cs               # Post-mission results
│       ├── IntelBriefing.cs                # Pre-mission intelligence
│       └── PersistenceManager.cs           # Save/load campaign state
│
├── DEADSKY.AI/  (renamed from IronDome.AI)
│   ├── Agents/
│   │   ├── ... (existing agents)
│   │   ├── EnemySquadronLeaderAgent.cs     ★ NEW — per-squadron tactical AI
│   │   └── CrewPersonalityAgent.cs         ★ NEW — gives crew unique voices
│   │
│   ├── Prompts/
│   │   ├── SystemPrompts/
│   │   │   ├── ... (existing)
│   │   │   ├── enemy_wing_commander.txt    ★ NEW
│   │   │   ├── enemy_squadron_leader.txt   ★ NEW
│   │   │   ├── crew_personality_calm.txt   ★ NEW
│   │   │   ├── crew_personality_nervous.txt ★ NEW
│   │   │   ├── crew_personality_veteran.txt ★ NEW
│   │   │   └── crew_personality_joker.txt  ★ NEW
│   │   └── EventPrompts/                   ★ NEW
│   │       ├── crew_panic.txt
│   │       ├── crew_heroism.txt
│   │       ├── crew_joke.txt
│   │       ├── civilian_encounter.txt
│   │       ├── pilot_bailout.txt
│   │       └── equipment_failure.txt
│   │
│   └── Tools/                              ★ NEW TOOLS
│       ├── GetCrewStatusTool.cs
│       ├── UpdateSoldierMoraleTool.cs
│       ├── TriggerCrewEventTool.cs
│       ├── GetBudgetTool.cs
│       ├── GetUpgradesTool.cs
│       ├── SetGroupFormationTool.cs
│       ├── CoordinateGroupAttackTool.cs
│       ├── EnemyLearnLessonTool.cs
│       └── GenerateCrewDialogueTool.cs
│
└── DEADSKY.App/  (renamed from IronDome.App)
    ├── Views/
    │   ├── ... (existing)
    │   ├── ShopView.xaml                    ★ NEW — Requisition Terminal
    │   ├── CrewManagementView.xaml           ★ NEW — Crew roster/assignment
    │   ├── SoldierDetailView.xaml            ★ NEW — Individual soldier card
    │   ├── MissionDebriefView.xaml           ★ NEW — Post-mission results
    │   ├── IntelBriefingView.xaml            ★ NEW — Pre-mission intel
    │   ├── CampaignMapView.xaml              ★ NEW — Campaign progress
    │   ├── PlayerProfileView.xaml            ★ NEW — Rank, merits, stats
    │   └── TrainingView.xaml                 ★ NEW — Training programs
    │
    ├── ViewModels/
    │   ├── ... (existing)
    │   ├── ShopViewModel.cs                 ★ NEW
    │   ├── CrewManagementViewModel.cs       ★ NEW
    │   ├── MissionDebriefViewModel.cs       ★ NEW
    │   └── CampaignViewModel.cs             ★ NEW
    │
    └── Controls/
        ├── ... (existing)
        ├── SoldierCard.cs                   ★ NEW — Soldier info card
        ├── MoraleBar.cs                     ★ NEW — Morale indicator
        ├── SkillRadarChart.cs               ★ NEW — Spider chart for skills
        ├── BudgetDisplay.cs                 ★ NEW — Budget counter
        └── ShopItemCard.cs                  ★ NEW — Shop item display
```

---

## G. UPDATED DEVELOPMENT PHASES

Add these phases to the original plan:

```
PHASE 5.5: PERSONNEL SYSTEM (after AI Integration, Week 11)
├── Soldier model and roster
├── Skill system and experience tracking
├── Morale and fear real-time calculation
├── Personality system (affects AI prompts)
├── Crew performance modifiers on gameplay
├── Crew management UI
├── Soldier detail view
├── Between-mission crew status screen
└── AI-generated crew dialogue (personality-driven)

PHASE 6.5: ENEMY GROUP AI (Week 12)
├── Squadron structure (wing/squadron/element)
├── Formation system (formation types, spacing)
├── Group coordination (synchronized attacks)
├── Commander profiles (personality, adaptation)
├── Tactic library (10 group tactics)
├── Enemy intelligence system (learning across missions)
├── Enemy RWR detection model
├── Integration with AI agent (enemy commander uses group tools)
└── Multi-group scenario testing

PHASE 7.5: ECONOMY & PROGRESSION (Week 13)
├── Budget system (earn, spend, track)
├── Shop catalog (all items defined)
├── Shop UI (Requisition Terminal)
├── Upgrade installation (timing, radar downtime)
├── Mission reward calculator
├── Player rank system
├── Merit/commendation system
├── Unlock conditions
├── Between-mission flow (debrief → crew → shop → brief → launch)
└── Save/load campaign state

PHASE 8.5: EVENTS SYSTEM (Week 14)
├── Event engine (probability, threshold, chain triggers)
├── All event definitions (60+ events)
├── Event-specific AI prompts
├── Atmosphere engine (tension, mood)
├── Audio integration with atmosphere
├── Event UI (notifications, popups, consequences)
├── Testing event variety and frequency
└── Balance event probabilities

PHASE 9 becomes PHASE 9.5: CAMPAIGN (Week 16)
├── Campaign structure (5 chapters, 20 missions)
├── Campaign manager (progression, state)
├── Between-mission UI flow
├── Mission debrief screen
├── Intel briefing screen
├── Campaign map view
├── Enemy commander escalation across campaign
├── Difficulty scaling
└── End-of-campaign narrative
```

### Updated Timeline:

```
Phase 1:     Foundation                      Weeks 1-2
Phase 2:     Core Radar & Tracking           Weeks 3-4
Phase 3:     Weapons System                  Weeks 5-6
Phase 4:     Communications                  Week 7
Phase 5:     AI Integration                  Weeks 8-10
Phase 5.5:   Personnel System                Week 11
Phase 6:     Terrain Map                     Week 12
Phase 6.5:   Enemy Group AI                  Week 13
Phase 7:     Audio                           Week 14
Phase 7.5:   Economy & Progression           Week 15
Phase 8:     Polish & Effects                Week 16
Phase 8.5:   Events System                   Week 17
Phase 9:     Scenarios                       Week 18
Phase 9.5:   Campaign                        Week 19
Phase 10:    Testing & Optimization          Week 20
                                             ──────────
                                             ~20 weeks total
```

---

## H. UPDATED FILE & SCOPE ESTIMATES

```
Total estimated files:     ~180 C# files  (was ~120)
                           ~35 XAML files  (was ~25)
                           ~20 prompt files (was ~6)
                           ~15 resource files
                           ~10 scenario/campaign JSON files
                           ~5 config files

Total estimated lines:     ~25,000-32,000 lines of C#  (was ~15-20k)
                           ~5,000-6,000 lines of XAML   (was ~3-4k)

AI tools count:            36 tools  (was 28)
Event types:               60+ unique events
Shop items:                80+ purchasable items
Soldier traits:            15+ personality/skill dimensions
Enemy tactics:             10+ group formations/strategies
```

---

**Both plans together form the complete DEADSKY blueprint. Copy this addendum alongside the original plan (with "Iron Dome" mentally replaced by "DEADSKY") — they're designed as one unified document.**

**When you're ready, say the word and we start building Phase 1.**