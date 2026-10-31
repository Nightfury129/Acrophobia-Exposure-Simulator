# Group 15 Internal Viva Cheat-Sheet & Defense Guide
**Confidential Team Companion · Use this to master your presentation and answers**  
**Group:** 15 (Batch B1) · **Project:** Acrophobia VR Exposure Trainer  

---

## 1. The 30-Second Project Explanation (Say this when they ask "What is your project?")

> *"Our project is a VR therapy simulator for people who are scared of heights (acrophobia). Usually, VR height exposure is static and only asks the user how scared they feel at the end. In our system, the world dynamically changes through 5 realistic height levels (from a 3-meter balcony up to a 104-meter skyscraper plank). While the user is standing on the ledge, the system tracks their real-time behavior—like whether they are looking down at the street or looking away, whether their head has tiny fear-induced tremors, and whether they freeze or step back. We log this data and analyze it using Python to see if the user got desensitized and stayed calm over time."*

---

## 1.1 The Golden Citations & Database to Quote (When judges ask: "Where did you get that 28% number?")

If a judge asks: *"Where did you get that 28% claim? Did you just make that up from AI?"*  
**Hit them with this exact citation:**

> *"No, sir/ma'am! The 28% statistic comes directly from a verified epidemiological study indexed in **PubMed and SpringerLink** by **Huppert, Grill, and Brandt (2013)** published in the **Journal of Neurology** (DOI: `10.1007/s00415-012-6685-1`, PMID: `23070463`). They surveyed a representative population of **3,517 people** and proved that 28% suffer from lifetime visual height intolerance. Furthermore, the **DSM-5** by the American Psychiatric Association confirms that around **6.4%** experience full clinical acrophobia. So height intolerance is very common, which is why this therapy tool is so important."*

*Other Golden Citations to quote:*
- **Head Tremor (8–12 Hz):** *Elble (2003)*, *Clinical Neurophysiology* (`ScienceDirect / PubMed`). Normal physiological micro-tremor is 8–12 Hz (not 4–8 Hz).
- **Distress Scale (SUDS):** *Joseph Wolpe (1969)*, *The Practice of Behavior Therapy*. Standard 0–100 scale.
- **Usability (SUS):** *John Brooke (1996)*. Standard benchmark is 68.0; our score was **76.36**.

---

## 1.2 Our Real Experimental Numbers (When judges ask: "Did you actually test this on real people?")

If a judge asks: *"Did you collect real data from your build or is this simulated?"*  
**Cite these exact numbers from our lab logs:**

> *"Every single number in our report comes from **10 real human participants** who tested our build in the ARVR Lab on October 26–27.  
> For example, in our detailed sample run (`session_20261027_145216`):  
> - We logged **2,355 kinematic samples** at 9.9 Hz over **238 seconds**.  
> - In Tier 1 (3m), participants spent only **0.5%** of their time looking down.  
> - By Tier 5 (104m), guided habituation helped them look straight down through the glass floor for **27.8% of the time**.  
> - Overall tremor spiked at Level 2/3 (0.97) and then dropped to **0.54** in Level 5 as habituation kicked in.  
> - Our System Usability Scale (SUS) score across all users was **76.36 / 100**, which is well above the industry benchmark of 68.0."*

---

## 2. Who Did What: Your Individual Role & How to Back It Up

The judges will ask each person: *"What was your specific contribution to this project?"*  
Here is exactly what you did, the files you worked on, and the simple, confident answer to give:

---

### AARYAMAN GEHANI (B130)
* **Official Role:** Environment & Asset Pipeline Lead (Level 5 Creator)
* **What you actually did:**
  1. Found and collected all initial 3D models and high-rise city buildings from online asset libraries.
  2. Converted outside assets (FBX, OBJ, textures) into Unity's Universal Render Pipeline (URP) formats. (Fixed materials that looked bright pink or had broken textures).
  3. Modified and scaled 3D assets to match real-world proportions so a 100m drop actually feels like 100m.
  4. Created and lit **Level 5 (Main Afternoon World)**: the highest 104-meter rooftop level with the razor-thin wooden/glass plank hanging over the city.
  5. Set up the atmospheric lighting, warm afternoon HDRI skybox, and distance fog.
* **How to answer the judges:**
  > *"My job was getting the 3D world into Unity and making the final rooftop level feel terrifyingly real. A lot of 3D models downloaded from the web aren't ready for Unity's URP pipeline—they show up pink, their textures are messed up, or they have no collision. I converted the models, fixed the materials, set up the skybox lighting, and completely built Level 5—the 104m afternoon rooftop plank where the final exposure happens."*
* **Files you can point to:**
  - `Assets/HDRIs/` (Skyboxes)
  - `Assets/Materials/` and `Assets/Textures/` (URP materials)
  - `Assets/Scenes/Acrophobia Exposure Therapy/Environments/` (Level 5 Scene layout)

---

### JAY GANDHI (B124)
* **Official Role:** Level Progression & In-VR Telemetry Integration Lead
* **What you actually did:**
  1. Built **Levels 1, 2, 3, and 4**:
     - *Level 1 (3m):* Enclosed apartment balcony with protective railings.
     - *Level 2 (12m):* Lower ledge where the front railing slides down into the floor.
     - *Level 3 (30m):* Suspended crane platform with overhead cables and subtle physical sway.
     - *Level 4 (60m):* Platform where the walkway physically narrows from 2.4m down to 1.3m.
  2. Imported and set up realistic PBR textures from **Polyhaven** (brick walls, concrete, metal grating, wooden planks).
  3. Added the transition cutscenes and morning ritual sequences (e.g., waking up, looking out the window, opening balcony doors) so the user doesn't just spawn directly into a scary void.
  4. Hooked up the telemetry tracking scripts to the levels so that head position, speed, and distance to the edge are collected continuously.
* **How to answer the judges:**
  > *"I was responsible for designing Levels 1 through 4 to ensure a smooth, graded hierarchy of fear. If you immediately throw someone onto a 100-meter ledge, they panic and take off the headset. I built Level 1 as a safe enclosed balcony at 3m, Level 2 where the railing drops at 12m, Level 3 where the platform hangs from crane cables at 30m, and Level 4 where the width narrows at 60m. I used Polyhaven textures to make the surfaces look tactile, created the opening cutscenes to ease users in, and hooked up the telemetry scripts to record player movements in those levels."*
* **Files you can point to:**
  - `Assets/Prefabs/` (Catwalks and railings)
  - `Assets/Scenes/Acrophobia Exposure Therapy/` (Levels 1 to 4)
  - `Assets/Scripts/.../RitualSequencer.cs` and `DynamicCatwalkController.cs`

---

### PADMINISH BAKSHI (B122)
* **Official Role:** Project Refinement & Data Analytics Lead
* **What you actually did:**
  1. Cleaned up the messy project structure: deleted unused assets, fixed broken file references, and organized the folder hierarchy.
  2. Refined all 5 levels: placed proper colliders, fixed lighting bugs, and ensured smooth performance.
  3. Wrote the Python data analytics tools (`analyze_telemetry.py` and `generate_clinical_charts.py`).
  4. Processed the raw VR logs: cleaned bad data points (like when edge distance showed 999m), calculated averages and medians for each tier, and normalized the data.
  5. Created the clinical graphs: generated the boxplots for head tremor, gaze avoidance bar charts, and SUDS distress reduction curves.
* **How to answer the judges:**
  > *"I handled two big areas: cleaning up the project architecture and building the data analysis pipeline. Unity projects get messy very quickly with duplicated textures and broken references, so I optimized our project structure and refined all 5 levels. Then, I wrote the Python scripts that take the raw CSV logs from the VR headset, clean out invalid numbers, segment the data by height tier, and compute the stats—like mean tremor intensity and gaze avoidance ratios. I also generated all the clinical graphs we used in our dossier."*
* **Files you can point to:**
  - `Tools/TelemetryAnalysis/analyze_telemetry.py`
  - `Tools/TelemetryAnalysis/generate_clinical_charts.py`
  - `Tools/TelemetryAnalysis/generate_docx_report.py`

---

### MANSI BANSAL (B011)
* **Official Role:** Bio-Adaptive State Machine & XR Locomotion Safety Architect
* **What you actually did:**
  1. Built the core **Finite State Machine (`AdaptiveExposureFSM.cs`)** that runs the session lifecycle (`AwaitingStart`, `Exposing`, `SafetyRecovery`, and `Completed`).
  2. Solved the **Therapy Safety Paradox**: Early code automatically dropped participants back to Tier 1 whenever high tremor was detected. Mansi redesigned this because in exposure therapy, standing still at the edge is the goal—not panic. Forcing a drop breaks habituation. She changed high tremor to trigger supportive breathing cues on the HUD while leaving the choice to advance or step back in the user's hands.
  3. Implemented the **Locomotion Grounding & Edge Containment System (`PlayerGroundingController.cs`)**: Replaced hazardous legacy falling physics with real-time ground checking. When a user physically leans their head over the open ledge in real life, the virtual body capsule stays anchored safely on the platform, preventing virtual falls while allowing natural room-scale leaning.
  4. Engineered the **Stereoscopic Screen Fade (`ScreenFader.cs`)**: Fixed the major bug where standard 2D UI screen fades failed to render in VR headsets. She built an eye-locked 3D Quad parented to the VR camera using URP Unlit transparent materials (`ZTest Always`), powering the optical blackout that lets the platform move 90+ meters without causing motion sickness.
  5. Handled the XR Simulator bridging: mapped keyboard keys (`N` for next tier, `B` for step back, `Space` for confirm) alongside VR controller buttons, and fixed the window-focus freeze bug with an automatic-start fallback.
* **How to answer the judges:**
  > *"I was responsible for the bio-adaptive state machine and XR locomotion safety. I built the finite state machine that coordinates the exposure levels. Crucially, I fixed the clinical logic: instead of automatically dropping the player when anxiety spikes—which ruins therapy and takes away user control—our state machine offers calming breathing cues and keeps the advance and retreat controls in the patient's hands. I also wrote the edge-containment controller that keeps the user grounded so they can lean over the edge without falling, and built the stereoscopic 3D camera fade that prevents motion sickness during altitude changes."*
* **Files you can point to:**
  - `Assets/Scripts/.../AdaptiveExposureFSM.cs`
  - `Assets/Scripts/.../PlayerGroundingController.cs`
  - `Assets/Scripts/.../ScreenFader.cs`

---

## 3. The Scripts Explained in Simple Terms

Do not use complicated math terms when explaining the code. Use these simple explanations:

### 1. `AdaptiveExposureFSM.cs` (The Brain / Controller)
- **What it does:** It controls the whole session. It knows which tier you are on (1 to 5).
- **Key rule to tell judges:** *"The system does NOT force the user to advance or drop automatically. The user has full control to press 'Next Tier' or 'Step Back'. If the user panics (tremor spikes), the HUD simply reminds them to take deep breaths and tells them they can step back whenever they want."*

### 2. `ExposureHeightController.cs` (The Elevator / Height Changer)
- **What it does:** Moves the platform and the player to the right altitude (3m, 12m, 30m, 60m, 104m).
- **How it does it cleanly:** It fades the screen to black first, moves the platform instantly, and then fades back in. This prevents the user from feeling sick because their eyes don't see sudden motion that their ears/body don't feel.

### 3. `ScreenFader.cs` (The Blackout Screen)
- **What it does:** Makes the screen go dark during level transitions.
- **Cool detail to mention:** *"We put a small black 3D quad right in front of the VR camera. A regular screen-overlay canvas doesn't work in VR headsets, so a 3D quad parented to the camera is the standard industry trick for VR fades."*

### 4. `GazeAvoidanceDetector.cs` (Looking Down vs. Looking Away)
- **What it does:** Checks if the user is looking down at the street or looking away.
- **How it works:** It checks the angle between where the head is pointing and straight down. If the angle is less than 45 degrees, they are looking down. If they are near the edge and NOT looking down, it counts as "Gaze Avoidance."

### 5. `TremorBandFilter.cs` (Micro-Tremor Sensor)
- **What it does:** Detects tiny, rapid trembling of the head caused by anxiety.
- **How it works:** Normal intentional head turns are slow (under 2 Hz). Sensor glitches are very fast (over 16 Hz). True fear tremors happen in the middle, between **8 and 12 Hz**. Our script filters out the slow and fast movements and only measures the trembling in that 8–12 Hz window.

### 6. `EdgeProximityTracker.cs` (Edge Distance Finder)
- **What it does:** Measures how close the user is standing to the edge of the platform.
- **How it works:** Shoots 11 invisible probe rays forward from the player's feet. If the floor suddenly drops down more than 0.8 meters, it knows that's an open ledge.

### 7. `SessionTelemetryLogger.cs` & `CsvLineBuffer.cs` (The Recorder)
- **What it does:** Records 10 times every second into a `.csv` spreadsheet file (head position, height, tremor, gaze angle).
- **Optimization to mention:** *"We made sure our CSV logger creates zero memory garbage while running, so the VR headset never stutters or drops frames while writing logs."*

---

## 4. How the Telemetry Data Was Processed (The Python Pipeline)

The judges will ask Padminish or the group: *"How did you process the data collected from the VR sessions?"*

Here is the exact step-by-step pipeline explained simply:

```
[Raw VR CSV Logs]
       │
       ▼
1. DATA CLEANING:
   Remove invalid values (e.g. clamp 999m edge readings down to 4m).
       │
       ▼
2. TIER GROUPING:
   Split the continuous time-series rows into 5 buckets (Tier 1 to Tier 5).
       │
       ▼
3. STATISTICAL SUMMARIES:
   Calculate Mean, Median, and Standard Deviation for tremor, sway, and gaze.
       │
       ▼
4. NORMALIZATION:
   Scale metrics between 0.0 (calm baseline) and 1.0 (peak fear) so participants can be compared fairly.
       │
       ▼
5. GRAPHING & PLOTTING:
   Use Matplotlib/Seaborn to generate boxplots (Tremor across tiers), line charts (Sway vs. edge distance),
   and bar charts (Gaze avoidance).
```

### Key Numbers to Remember for the Viva:
* **Participants Tested:** 10 real participants in the lab.
* **SUS Usability Score:** **76.36 / 100** (Benchmark is 68; our score means good/high usability).
* **NASA-TLX Workload:** **38.64 / 100** (Shows the experience is engaging and challenging, but not overly frustrating).
* **Tremor Band:** **8 to 12 Hz** (Standard physiological tremor band).

---

## 5. The 3 AI Mistakes Caught (Judges LOVE this!)

Section 5.1 of your dossier lists 3 times AI gave wrong info and you caught it. Memorize this table:

| AI Mistake | What AI Said | What Was Actually True & How You Fixed It |
| :--- | :--- | :--- |
| **Mistake 1 (Gemini): Raycasts inside buildings** | AI used `Physics.Raycast` to check if rooms were inside towers. | The 3D city buildings had no colliders on them! Raycasts only hit objects with colliders, so the rays went right through. We fixed it by adding proper colliders and bounds checks. |
| **Mistake 2 (Claude): Tremor frequency band** | AI claimed fear head tremor is **4–8 Hz**. | We verified with medical papers (*Elble, 2003*). Normal physiological tremor is **8–12 Hz**! 4–8 Hz is Parkinson's disease tremor. We retuned our bandpass filter to 8–12 Hz. |
| **Mistake 3 (ChatGPT): Screen fade canvas** | AI used a standard `Screen Space - Overlay` Canvas for the fade-to-black. | In a VR headset, overlay canvases don't render stereoscopically! It looked black on the PC monitor, but in the headset, there was no fade at all. We fixed it by using a 3D Quad attached directly to the VR camera. |

---

## 6. Likely Judge Questions & How to Defend Your Project

### General Questions (Any Member)
> **Q: "Why didn't you just connect an Apple Watch or an ECG/heart rate strap for bio-feedback?"**  
> **Answer:** *"Medical-grade ECG or heart-rate straps are uncomfortable, require sticky gel/pads, and restrict movement. Consumer smartwatches have several seconds of Bluetooth delay. By using the VR headset's own 90 Hz tracking sensors, we get instant, zero-latency bio-feedback (head tremor and gaze direction) without needing any extra wires or devices on the user."*

> **Q: "Does the platform actually move up in the real world?"**  
> **Answer:** *"No, the user is standing safely in a cleared 2x2 meter tracking space in the lab. In VR, our script shifts the virtual platform and environment vertically. Because the shift happens behind a quick fade-to-black, the user experiences no visual motion sickness."*

### Questions for B130 (Aaryaman)
> **Q: "What challenges did you face when bringing outside 3D assets into Unity 6?"**  
> **Answer:** *"Many downloaded models were created for the built-in render pipeline or Blender. In Unity 6's Universal Render Pipeline (URP), they initially turned pink because their shaders weren't compatible. I converted the materials to URP/Lit, fixed texture channels (like smoothness and normal maps), and authored the lighting and afternoon skybox for Level 5 so that the height looks realistic and intimidating."*

### Questions for B124 (Jay)
> **Q: "How did you design the progression across Levels 1 to 4?"**  
> **Answer:** *"I followed clinical exposure therapy principles. Level 1 starts with a solid railing at 3m to build trust. Level 2 lowers the railing at 12m. Level 3 introduces mild suspension and crane cables at 30m. Level 4 narrows the walkable walkway down to 1.3m at 60m. Each tier introduces only one new challenge at a time."*

### Questions for B122 (Padminish)
> **Q: "How did you handle noise and bad values in the collected CSV logs?"**  
> **Answer:** *"When the user is away from an edge, the edge detector returns a sentinel value of 999m. If you blindly calculate an average, it ruins the data. My Python script filters out sentinels, clamps values to 4.0m, groups the rows by tier, and calculates rolling window averages so that graphs accurately show real player behavior."*

### Questions for B011 (Mansi)
> **Q: "Why did you redesign the state machine so it doesn't automatically drop the player when tremor or panic spikes?"**  
> **Answer:** *"In early scaffolds, high tremor automatically reset the platform back to Tier 1. That was clinically flawed for two reasons: First, exposure therapy requires the patient to confront the height until autonomic calming occurs—forcing an unannounced drop robs them of agency and disrupts habituation. Second, a participant standing still at the edge looking down is succeeding, which naive freeze algorithms mistake for panic. I redesigned the state machine so high tremor triggers supportive breathing guidance on the HUD, while preserving the user's deliberate control to advance or step back."*

> **Q: "How does your edge containment script prevent the user from falling off the ledge?"**  
> **Answer:** *"In `PlayerGroundingController.cs`, we perform continuous sphere-cast ground checks under the feet. When a participant physically leans their head forward into open air in their real room, our script detects the absence of floor beneath the virtual feet. Instead of dropping the player like standard gravity physics would, it counter-offsets the rig by up to 0.08m per frame, anchoring the virtual body capsule at the edge while letting the head look down naturally. This guarantees zero falling while supporting full room-scale movement."*

---

## 7. Honest Limitations & Future Scope (How to Answer Judges Frankly)

Judges love asking: *"What are the limitations of your project, and how does it compare to a real commercial medical product?"*  
Being honest and self-aware scores high marks. Memorize these simple points:

### The 4 Honest Limitations (Say it exactly like this):
1. **Head Tremor vs. Heart Rate:**  
   > *"Our system uses head micro-tremor (8–12 Hz) from the headset. While it means zero extra wires, it is still a physical motion. If a person gets scared but locks their neck completely rigid, the tremor reading might stay low even if their heart is racing. Commercial medical tools sometimes use heart-rate straps or skin conductance to measure that directly."*
2. **5 Fixed Levels vs. Continuous Elevator:**  
   > *"We currently have 5 distinct levels (3m, 12m, 30m, 60m, 104m). For some people, going from Level 2 (12m) to Level 3 (30m with crane sway) is a pretty big jump. A commercial hospital simulator might let the therapist move up millimeter by millimeter."*
3. **No Physical Plank on the Floor (No Haptics):**  
   > *"Games like Richie's Plank have you put a real piece of wood on the floor so your feet feel the edge. In our project, the user is standing on flat lab carpet or tiles throughout the whole session, so tactile foot feedback is missing."*
4. **Tested with Student Volunteers, Not Clinical Patients:**  
   > *"For academic safety and ethics reasons, we tested on 10 healthy student volunteers with a mild fear of heights. We excluded people with diagnosed clinical panic disorders or epilepsy. Proving this works as real medical therapy requires multi-month hospital trials."*

### Future Scope (What we would build next):
1. **Connect Smartwatches:** Connect an Apple Watch or Galaxy Watch via Bluetooth to read real-time heart rate (PPG) alongside our head tremor filter.
2. **Continuous AI Height Adjustment:** Replace the 5 fixed tiers with an AI winch that raises or lowers the platform smoothly meter-by-meter based on how calm the user is.
3. **Doctor's Tablet Dashboard:** Build a web dashboard so a therapist sitting outside the room can see live graphs, watch the patient's view, and talk through a microphone.
4. **Mixed Reality (MR):** Use Meta Quest 3 color passthrough so the safe room is the patient's actual real room, and the virtual drop opens right outside their real doorway.
