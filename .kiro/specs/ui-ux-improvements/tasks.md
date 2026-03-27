# Implementation Plan: UI/UX Improvements

## Overview

This implementation plan transforms the DEADSKY tactical air defense game UI from cramped to polished AAA-quality. The approach focuses on three core areas: (1) collapsible panel system with auto-expand, (2) comprehensive spacing improvements across all UI sections, and (3) event-driven AI communication system. All changes preserve existing functionality while enhancing visual hierarchy and user experience.

## Tasks

- [ ] 1. Create collapsible panel infrastructure
  - [ ] 1.1 Create CollapsibleSection custom control
    - Implement WPF UserControl with Header and Content properties
    - Add IsExpanded DependencyProperty with two-way binding support
    - Implement chevron icon rotation animation (0° expanded, 180° collapsed)
    - Add smooth height animation using DoubleAnimation with EaseInOut easing (250ms duration)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6, 1.7, 1.8_
  
  - [ ] 1.2 Create CollapsibleSectionViewModel base class
    - Implement INotifyPropertyChanged for IsExpanded property
    - Add session-based state persistence using Dictionary<string, bool>
    - Implement auto-expand trigger method with border pulse animation support
    - Add SectionId property for state tracking
    - _Requirements: 1.5, 1.9, 2.5, 2.6_
  
  - [ ]* 1.3 Write unit tests for CollapsibleSection control
    - Test IsExpanded property binding
    - Test animation trigger on state change
    - Test chevron rotation
    - _Requirements: 1.1, 1.2, 1.7, 1.8_

- [ ] 2. Implement right panel collapsible sections
  - [ ] 2.1 Convert Scope Control section to CollapsibleSection
    - Wrap existing Scope Control content in CollapsibleSection control
    - Set SectionId="ScopeControl" and Header="SCOPE CONTROL"
    - Bind IsExpanded to ViewModel property
    - Initialize IsExpanded=true on first launch
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.9_
  
  - [ ] 2.2 Convert Fire Control section to CollapsibleSection
    - Wrap existing Fire Control content in CollapsibleSection control
    - Set SectionId="FireControl" and Header="FIRE CONTROL"
    - Bind IsExpanded to ViewModel property
    - Add auto-expand trigger for "WEAPONS TIGHT" and "WEAPONS FREE" status changes
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.9, 2.2_
  
  - [ ] 2.3 Convert Weapons/Loadout section to CollapsibleSection
    - Wrap existing Weapons/Loadout content in CollapsibleSection control
    - Set SectionId="WeaponsLoadout" and Header="WEAPONS/LOADOUT"
    - Bind IsExpanded to ViewModel property
    - Add auto-expand trigger for weapon reload completion events
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.9, 2.3_
  
  - [ ] 2.4 Convert Engagement Control section to CollapsibleSection
    - Wrap existing Engagement Control content in CollapsibleSection control
    - Set SectionId="EngagementControl" and Header="ENGAGEMENT CONTROL"
    - Bind IsExpanded to ViewModel property
    - Add auto-expand trigger for new threat detection events
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.9, 2.1_
  
  - [ ]* 2.5 Write integration tests for collapsible sections
    - Test all 4 sections collapse/expand correctly
    - Test state persistence during session
    - Test auto-expand triggers fire correctly
    - _Requirements: 1.1, 1.2, 1.5, 2.1, 2.2, 2.3_

- [ ] 3. Checkpoint - Verify collapsible panels work correctly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 4. Implement spacing improvements for right panel
  - [ ] 4.1 Update Scope Control section spacing
    - Change section Padding from "10" to "16"
    - Change UniformGrid button Margin from "6" to "10"
    - Update XAML in MainWindow.xaml for Scope Control section
    - _Requirements: 3.1, 3.2, 3.3, 3.7_
  
  - [ ] 4.2 Update Fire Control section spacing
    - Change section Padding from "10" to "16"
    - Change TextBlock Margin from "4,8" to "8,12"
    - Update section-to-section Margin from "8" to "16"
    - Update XAML in MainWindow.xaml for Fire Control section
    - _Requirements: 3.1, 3.2, 3.4, 3.7_
  
  - [ ] 4.3 Update Weapons/Loadout section spacing
    - Change section Padding from "10" to "16"
    - Change weapon card Margin from "6" to "10"
    - Update section-to-section Margin from "8" to "16"
    - Update XAML in MainWindow.xaml for Weapons/Loadout section
    - _Requirements: 3.1, 3.2, 3.5, 3.7_
  
  - [ ] 4.4 Update Engagement Control section spacing
    - Change section Padding from "10" to "16"
    - Change chip Border Margin to minimum "8"
    - Update section-to-section Margin from "8" to "16"
    - Update XAML in MainWindow.xaml for Engagement Control section
    - _Requirements: 3.1, 3.2, 3.6, 3.7_

- [ ] 5. Implement spacing improvements for Track Board
  - [ ] 5.1 Update Track Board row spacing
    - Change track row Padding from "7,5" to "10,8"
    - Change Margin between rows from "4" to "8"
    - Change internal text spacing from "4" to "6"
    - Add 1px BorderThickness with subtle separator color
    - Update hover highlight area to match new padding
    - Update XAML in MainWindow.xaml or TrackBoard control
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

- [ ] 6. Implement Mission Board collapsible section and spacing
  - [ ] 6.1 Convert Mission Board to CollapsibleSection
    - Wrap existing Mission Board content in CollapsibleSection control
    - Set SectionId="MissionBoard" and Header="MISSION BOARD"
    - Bind IsExpanded to ViewModel property
    - Add auto-expand trigger for alert level "HIGH" or "CRITICAL"
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.9, 2.4_
  
  - [ ] 6.2 Update Mission Board spacing and visual hierarchy
    - Change metric card Padding to "12"
    - Change Margin between metric cards from "6" to "10"
    - Change metric value FontSize to "18pt"
    - Change spacing between label and value from "4" to "8"
    - Add alternating background colors to metric cards
    - Change sector posture card Padding from "10" to "16"
    - Change text spacing within sector posture from "6" to "10"
    - Update XAML in MainWindow.xaml for Mission Board
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

- [ ] 7. Checkpoint - Verify spacing improvements
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Create event-driven AI communication infrastructure
  - [ ] 8.1 Create GameEventType enumeration
    - Define enum values for all 10 event types from requirements
    - Include: ThreatGroupDetected, ThreatLevelEscalated, BatteryDamaged, SupportAvailable, MissionPhaseChanged, HighKillStreak, CoordinatedAttackDetected, ThreatClassificationChanged, NewAircraftTypeDetected, EnemyBehaviorChanged
    - Place in DEADSKY.Core/Events namespace
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10_
  
  - [ ] 8.2 Create GameEvent class
    - Add EventType property (GameEventType enum)
    - Add Timestamp property (DateTime)
    - Add EventData property (Dictionary<string, object> for flexible data)
    - Add Priority property (Urgent or Routine)
    - Implement IEquatable<GameEvent> for comparison
    - Place in DEADSKY.Core/Events namespace
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 6.4_
  
  - [ ] 8.3 Create AIMessageCooldownManager class
    - Implement per-agent cooldown tracking (30 seconds minimum)
    - Implement global cooldown tracking (10 seconds minimum)
    - Add CanSendMessage(agentName) method checking both cooldowns
    - Add RecordMessage(agentName) method updating cooldown timers
    - Add ResetCooldowns() method for player-initiated message responses
    - Place in DEADSKY.Core/Comms namespace
    - _Requirements: 6.1, 6.2, 6.5_
  
  - [ ] 8.4 Create AIMessageQueue class
    - Implement priority queue with urgent and routine message separation
    - Add EnqueueMessage(GameEvent, message) method
    - Add DequeueNextMessage() method respecting cooldowns and priority
    - Add DiscardStaleMessages() method removing routine messages older than 60 seconds
    - Implement queue processing with cooldown spacing
    - Place in DEADSKY.Core/Comms namespace
    - _Requirements: 6.3, 6.4, 6.6, 6.7_
  
  - [ ]* 8.5 Write unit tests for AI communication infrastructure
    - Test cooldown enforcement (per-agent and global)
    - Test priority queue ordering
    - Test stale message discard
    - Test cooldown reset on player message
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

- [ ] 9. Implement game event detection and AI message triggers
  - [ ] 9.1 Implement threat group detection event
    - Add detection logic in threat tracking system for 2+ contacts within 30 seconds
    - Raise GameEvent with EventType.ThreatGroupDetected
    - Trigger AlliedHQ message via AIMessageQueue
    - _Requirements: 5.1_
  
  - [ ] 9.2 Implement threat level escalation event
    - Add detection logic for threat level changes (medium→high, high→critical)
    - Raise GameEvent with EventType.ThreatLevelEscalated
    - Trigger AlliedHQ message via AIMessageQueue
    - _Requirements: 5.2_
  
  - [ ] 9.3 Implement battery damage event
    - Add detection logic in damage system for player battery damage
    - Raise GameEvent with EventType.BatteryDamaged
    - Trigger AlliedHQ message via AIMessageQueue with Urgent priority
    - _Requirements: 5.3, 6.4_
  
  - [ ] 9.4 Implement support availability event
    - Add detection logic for friendly support becoming available
    - Raise GameEvent with EventType.SupportAvailable
    - Trigger AlliedHQ message via AIMessageQueue
    - _Requirements: 5.4_
  
  - [ ] 9.5 Implement mission phase change event
    - Add detection logic for mission phase transitions
    - Raise GameEvent with EventType.MissionPhaseChanged
    - Trigger AlliedHQ message via AIMessageQueue
    - _Requirements: 5.5_
  
  - [ ] 9.6 Implement high kill streak event
    - Add detection logic for 3+ kills within 120 seconds
    - Raise GameEvent with EventType.HighKillStreak
    - Trigger AlliedHQ message via AIMessageQueue
    - _Requirements: 5.6_
  
  - [ ] 9.7 Implement coordinated attack detection event
    - Add detection logic for 3+ threats from different vectors
    - Raise GameEvent with EventType.CoordinatedAttackDetected
    - Trigger Intel message via AIMessageQueue
    - _Requirements: 5.7_
  
  - [ ] 9.8 Implement threat classification change event
    - Add detection logic for classification changes (unknown→hostile)
    - Raise GameEvent with EventType.ThreatClassificationChanged
    - Trigger Intel message via AIMessageQueue
    - _Requirements: 5.8_
  
  - [ ] 9.9 Implement new aircraft type detection event
    - Add detection logic for first-time aircraft type detection in mission
    - Raise GameEvent with EventType.NewAircraftTypeDetected
    - Trigger Intel message via AIMessageQueue
    - _Requirements: 5.9_
  
  - [ ] 9.10 Implement enemy behavior change event
    - Add detection logic for significant behavior changes
    - Raise GameEvent with EventType.EnemyBehaviorChanged
    - Trigger Intel message via AIMessageQueue
    - _Requirements: 5.10_
  
  - [ ]* 9.11 Write integration tests for event detection
    - Test each event type triggers correctly
    - Test AI messages are queued with correct priority
    - Test cooldown system prevents spam
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 6.1, 6.2, 6.3_

- [ ] 10. Checkpoint - Verify event-driven AI communication
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Implement enhanced color coding for threats
  - [ ] 11.1 Create ThreatColorCalculator class
    - Implement GetThreatColor(range, isInbound, weaponsHot) method
    - Return #DC3545 for <20nm AND inbound AND weapons hot (critical)
    - Return #FD7E14 for 20-40nm AND inbound (high)
    - Return #FFC107 for 40-80nm AND tracking (medium)
    - Return #28A745 for >80nm (low)
    - Verify all colors meet WCAG AA contrast ratio (4.5:1)
    - Place in DEADSKY.App/Services namespace
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
  
  - [ ] 11.2 Create ThreatColorConverter for XAML binding
    - Implement IValueConverter interface
    - Accept threat object and return SolidColorBrush
    - Use ThreatColorCalculator for color determination
    - Place in DEADSKY.App/Converters namespace
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.6_
  
  - [ ] 11.3 Apply threat colors to Track Board UI
    - Bind track row BorderBrush to ThreatColorConverter
    - Bind track ID TextBlock Foreground to ThreatColorConverter
    - Bind threat level indicator Background to ThreatColorConverter
    - Update XAML in Track Board control
    - _Requirements: 7.6, 7.7_
  
  - [ ]* 11.4 Write unit tests for threat color calculation
    - Test all 4 color ranges return correct values
    - Test real-time updates as range changes
    - Test WCAG AA contrast compliance
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.7_

- [ ] 12. Implement radio message visual improvements
  - [ ] 12.1 Add timestamp display to radio messages
    - Add Timestamp property to RadioMessage model
    - Create TimestampConverter formatting as "HH:MM:SS"
    - Add TextBlock for timestamp in message template
    - Update XAML in Comms Window control
    - _Requirements: 8.1_
  
  - [ ] 12.2 Implement channel color coding
    - Create ChannelColorConverter mapping channels to colors
    - Map Command Net → #4D8EC0 (blue)
    - Map Intel Net → #9B59B6 (purple)
    - Map Battery Net → #28A745 (green)
    - Map Guard → #DC3545 (red)
    - Bind message Border BorderBrush to ChannelColorConverter
    - Update XAML in Comms Window control
    - _Requirements: 8.2_
  
  - [ ] 12.3 Implement urgent message highlighting
    - Add IsUrgent property to RadioMessage model
    - Create pulsing border animation (3 seconds duration)
    - Trigger animation on urgent message arrival
    - Update XAML in Comms Window control
    - _Requirements: 8.3_
  
  - [ ] 12.4 Implement smart auto-scroll behavior
    - Track user scroll position in Comms Window
    - Auto-scroll to bottom when new message arrives AND user within 100px of bottom
    - Show "Scroll to Bottom" button when user scrolls up >100px
    - Implement smooth scroll animation (300ms) on button click
    - Add logic in Comms Window code-behind or ViewModel
    - _Requirements: 8.4, 8.5, 8.6_
  
  - [ ] 12.5 Update radio message spacing
    - Change message bubble Padding from "8,6" to "12,8"
    - Change Margin between messages from "6" to "10"
    - Update XAML in Comms Window control
    - _Requirements: 8.7, 8.8_

- [ ] 13. Implement interactive feedback enhancements
  - [ ] 13.1 Create consistent button hover/press styles
    - Define Button style with hover background color change (50ms transition)
    - Define Button style with pressed state visual (minimum 100ms)
    - Add smooth 100ms transitions for all state changes
    - Add to App.xaml or Themes/Generic.xaml
    - _Requirements: 10.1, 10.3, 10.6, 10.7_
  
  - [ ] 13.2 Create collapsible header hover style
    - Define hover cursor (Hand) for collapsible headers
    - Add subtle highlight on hover
    - Add smooth 100ms transition
    - Add to CollapsibleSection control style
    - _Requirements: 10.2, 10.6, 10.7_
  
  - [ ] 13.3 Create track row hover effect
    - Increase BorderBrush brightness by 20% on hover
    - Add smooth 100ms transition
    - Update Track Board control style
    - _Requirements: 10.4, 10.7_
  
  - [ ] 13.4 Create weapon card hover effect
    - Add subtle glow effect on hover using DropShadowEffect
    - Add smooth 100ms transition
    - Update Weapons/Loadout control style
    - _Requirements: 10.5, 10.7_

- [ ] 14. Final integration and wiring
  - [ ] 14.1 Wire all collapsible sections to ViewModels
    - Connect all 5 CollapsibleSection controls to corresponding ViewModel properties
    - Ensure state persistence works across all sections
    - Verify auto-expand triggers are connected to game events
    - _Requirements: 1.1, 1.2, 1.5, 2.1, 2.2, 2.3, 2.4_
  
  - [ ] 14.2 Wire event detection to AI message system
    - Connect all 10 game event detections to AIMessageQueue
    - Ensure cooldown manager is integrated with message queue
    - Verify priority handling for urgent vs routine messages
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 6.1, 6.2, 6.3, 6.4_
  
  - [ ] 14.3 Apply all spacing changes to MainWindow.xaml
    - Verify all right panel spacing updates are applied
    - Verify Track Board spacing updates are applied
    - Verify Mission Board spacing updates are applied
    - Verify radio message spacing updates are applied
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 4.1, 4.2, 4.3, 4.4, 4.5, 8.7, 8.8, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_
  
  - [ ] 14.4 Apply all visual enhancements
    - Verify threat color coding is applied to Track Board
    - Verify radio message timestamps and channel colors are displayed
    - Verify all hover/press states are working
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 8.1, 8.2, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  
  - [ ]* 14.5 Write end-to-end integration tests
    - Test complete user flow with collapsible panels
    - Test event-driven AI communication in realistic scenarios
    - Test visual feedback across all interactive elements
    - _Requirements: All requirements_

- [ ] 15. Final checkpoint - Comprehensive testing
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at logical breaks
- Implementation uses C# and WPF/XAML for the DEADSKY tactical air defense game
- All spacing values are explicitly specified to match requirements (10px→16px, 4px→8px, etc.)
- Event-driven AI communication prevents message spam through cooldown system
- Color coding meets WCAG AA accessibility standards
- All animations use GPU-accelerated WPF animations for 60fps performance
