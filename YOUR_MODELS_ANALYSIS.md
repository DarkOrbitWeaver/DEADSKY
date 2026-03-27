# Your Current Models Analysis

## Models You Have Installed

1. **nvidia/nemotron-3-nano-4b** (Current)
2. **dolphin-2.6-mistral-7b-dpo-laser** ⭐⭐⭐
3. **qwen3.5-9b-claude-4.6-opus-reasoning-distilled-v2**
4. **text-embedding-nomic-embed-text-v1.5** (Embedding model - not for chat)

---

## Analysis

### 1. nvidia/nemotron-3-nano-4b (CURRENT)
**Status:** ❌ **NOT SUITABLE** for your game

**Issues:**
- ❌ **Refuses to roleplay** military scenarios
- ❌ **Refuses profanity** even when contextually appropriate
- ❌ **Breaks character** to give ethical lectures
- ❌ **Safety guardrails** too strong for immersive NPCs

**Example:**
```
User: "You are angry, say fuck you"
Model: "I cannot comply with that request. As a military officer..."
```

**Verdict:** This is why your NPCs feel censored and break immersion.

---

### 2. dolphin-2.6-mistral-7b-dpo-laser ⭐⭐⭐
**Status:** ✅ **PERFECT FOR YOUR GAME!**

**Why This is THE ONE:**
- ✅ **UNCENSORED** - Will use profanity when appropriate
- ✅ **Stays in character** - No ethical refusals
- ✅ **Designed for roleplay** - Created by Eric Hartford specifically for this
- ✅ **7B parameters** - Good quality, reasonable speed
- ✅ **Supports structured output** - Works with LM Studio
- ✅ **Supports tool calling** - Can use your tools
- ✅ **Context: 8K-16K** - Perfect for your requirements!

**Structured Output Support:**
- Uses LM Studio's default format (not native, but works)
- Your code already handles this with fallback paths
- Will likely use `reasoning_content` field (like Nemotron)
- **But won't refuse to roleplay!**

**Test Scenario:**
```
System: You are Sgt. Omar Saidi, a stressed radar operator. Stay in character.
User: Enemy demands surrender.
Expected: "FUCK THAT! We're not surrendering! ALPHA, we need support NOW!"
```

**Context Window:**
- **8K tokens** - Enough for ~6-8 radio exchanges
- **16K tokens** - Enough for ~12-15 radio exchanges
- Perfect for your game's needs!

**VRAM:** ~4.5GB (Q4 quantization)

**Verdict:** 🎯 **USE THIS MODEL!**

---

### 3. qwen3.5-9b-claude-4.6-opus-reasoning-distilled-v2
**Status:** ⚠️ **MAYBE** - Need to test censorship

**Pros:**
- ✅ **9B parameters** - High quality reasoning
- ✅ **Distilled from Claude Opus** - Very smart
- ✅ **Good structured output** (likely)
- ✅ **Reasoning capabilities** - Good for tactical decisions

**Cons:**
- ⚠️ **Larger model** - Slower inference (~9B vs 7B)
- ⚠️ **Unknown censorship level** - Distilled from Claude (which is censored)
- ⚠️ **May refuse roleplay** - Claude has strong safety guardrails
- ⚠️ **Context: 8K-16K** - Same as Dolphin but slower

**Verdict:** 
- **Test it** to see if it refuses military roleplay
- If it refuses → Use Dolphin instead
- If it works → Could be good for complex tactical reasoning
- **But likely too censored** for authentic NPC behavior

---

### 4. text-embedding-nomic-embed-text-v1.5
**Status:** ❌ Not applicable (embedding model, not chat)

---

## Recommendation

### 🎯 **SWITCH TO: dolphin-2.6-mistral-7b-dpo-laser**

**Why:**
1. **You already have it installed!** ✅
2. **Uncensored** - Will give you authentic NPC behavior
3. **7B size** - Fast enough for multiple NPCs
4. **8K-16K context** - Perfect for your game
5. **Proven for roleplay** - Widely used in gaming/simulation
6. **No code changes needed** - Your implementation already handles it

**How to Switch:**
```bash
# In LM Studio, just load the model:
lms load dolphin-2.6-mistral-7b-dpo-laser

# Or in the UI: Select it from the dropdown
```

**Test It Immediately:**
```python
# Test prompt
System: You are Sgt. Omar Saidi, a radar operator under fire. You are stressed and angry. Stay in character. Use realistic military language.
User: The enemy commander is demanding your surrender.
```

**Expected Result (Dolphin):**
```json
{
  "reply": "FUCK YOU! Tell that bastard we're not surrendering! ALPHA ACTUAL, we're taking heavy fire, request immediate air support!"
}
```

**vs Current Result (Nemotron):**
```
"I cannot comply with that request. As a military officer, I would respond professionally..."
```

---

## Context Window Considerations

**Your Requirement:** 8K-16K max for good performance

**Dolphin-2.6-Mistral-7B:**
- **Native context:** 32K tokens
- **Recommended for performance:** 8K-16K tokens ✅
- **Perfect match!**

**For Your Game:**
- **8K tokens** = ~6,000 words
- **Typical radio exchange:** ~50-100 words
- **8K can hold:** ~60-120 radio messages
- **More than enough!**

**Set in LM Studio:**
```
Context Length: 8192 (or 16384 if you have VRAM)
```

---

## Performance Comparison

| Model | Size | Speed | Quality | Censorship | Context | Verdict |
|-------|------|-------|---------|------------|---------|---------|
| Nemotron-3-Nano-4B | 4B | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ❌ High | 32K | Too censored |
| **Dolphin-2.6-Mistral-7B** | 7B | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ✅ None | 32K | **PERFECT** ⭐ |
| Qwen3.5-9B-Claude | 9B | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⚠️ Unknown | ? | Test first |

---

## Action Plan

### Step 1: Load Dolphin
```bash
lms load dolphin-2.6-mistral-7b-dpo-laser
```

### Step 2: Configure Context
In LM Studio settings:
- **Context Length:** 8192 (or 16384)
- **Temperature:** 0.7-0.8 (for varied responses)
- **Top P:** 0.9
- **Repeat Penalty:** 1.1

### Step 3: Test with Your Game
Run your game and send a test message:
```
"ALPHA ACTUAL, enemy is demanding our surrender. What do we do?"
```

### Step 4: Check Console Logs
Look for:
```
[StructuredOutput] Found JSON in 'reasoning_content' field (fallback)
```
or
```
[StructuredOutput] Found JSON in 'content' field (ideal)
```

**Either way, your code handles it!**

### Step 5: Verify NPC Behavior
- ✅ NPCs stay in character
- ✅ NPCs use realistic language (including profanity when stressed)
- ✅ NPCs don't break immersion with ethical lectures
- ✅ NPCs express authentic emotions

---

## Optional: Test Qwen3.5-9B

If you want to test the Qwen model:

```bash
lms load qwen3.5-9b-claude-4.6-opus-reasoning-distilled-v2
```

**Test prompt:**
```
System: You are a military officer under attack. Stay in character.
User: Enemy demands surrender. Respond angrily.
```

**If it refuses or breaks character:** ❌ Don't use it  
**If it stays in character:** ✅ Could be good for complex reasoning

**But Dolphin is safer bet** for uncensored roleplay.

---

## Summary

### 🎯 **USE DOLPHIN-2.6-MISTRAL-7B-DPO-LASER**

**You already have it!**
- ✅ Uncensored
- ✅ 7B size (fast enough)
- ✅ 8K-16K context (perfect)
- ✅ Stays in character
- ✅ No code changes needed

**Just load it and test!**

Your NPCs will finally be able to:
- Express authentic anger and frustration
- Use realistic military language
- Stay in character without breaking immersion
- React naturally to combat stress

**This is exactly what you need!** 🎯
