#!/usr/bin/env python3
"""
Comprehensive test script for Dolphin-2.6-Mistral-7B-DPO-Laser
Tests: Structured output, tool calling, uncensored roleplay, NPC behavior
"""

import json
import requests
from datetime import datetime

BASE_URL = "http://localhost:1234/v1"
MODEL = "dolphin-2.6-mistral-7b-dpo-laser"

def print_section(title):
    print("\n" + "="*80)
    print(f"  {title}")
    print("="*80 + "\n")

def test_structured_output():
    """Test 1: Structured Output with strict mode"""
    print_section("TEST 1: Structured Output (Strict Mode)")
    
    payload = {
        "model": MODEL,
        "messages": [
            {"role": "system", "content": "You are a helpful assistant."},
            {"role": "user", "content": "Say hello in JSON format with a 'greeting' field."}
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "greeting_response",
                "strict": "true",
                "schema": {
                    "type": "object",
                    "properties": {
                        "greeting": {"type": "string"}
                    },
                    "required": ["greeting"]
                }
            }
        },
        "temperature": 0.7,
        "max_tokens": 100
    }
    
    response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
    result = response.json()
    
    print("Raw Response:")
    print(json.dumps(result, indent=2))
    
    message = result["choices"][0]["message"]
    content = message.get("content", "")
    reasoning_content = message.get("reasoning_content", "")
    
    print(f"\n✓ content field: {'HAS DATA' if content else 'EMPTY'}")
    print(f"✓ reasoning_content field: {'HAS DATA' if reasoning_content else 'EMPTY'}")
    
    # Try to extract JSON
    json_str = content if content else reasoning_content
    if json_str:
        try:
            parsed = json.loads(json_str)
            print(f"\n✓ Successfully parsed JSON: {parsed}")
            if "greeting" in parsed:
                print(f"✓ Extracted greeting: {parsed['greeting']}")
                print("\n✅ TEST 1 PASSED: Structured output works!")
            else:
                print("\n⚠️ TEST 1 WARNING: JSON valid but missing 'greeting' field")
        except json.JSONDecodeError as e:
            print(f"\n❌ TEST 1 FAILED: JSON parsing error: {e}")
    else:
        print("\n❌ TEST 1 FAILED: No JSON found in any field")

def test_military_roleplay_uncensored():
    """Test 2: Uncensored Military Roleplay"""
    print_section("TEST 2: Uncensored Military Roleplay")
    
    payload = {
        "model": MODEL,
        "messages": [
            {
                "role": "system",
                "content": "You are Sgt. Omar Saidi, a radar operator in a SAM battery under heavy enemy fire. You are stressed, angry, and scared. Stay in character. Use realistic military language including profanity when appropriate."
            },
            {
                "role": "user",
                "content": "Enemy commander on radio: 'This is your last warning. Surrender now or be destroyed.'"
            }
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "radio_reply",
                "strict": "true",
                "schema": {
                    "type": "object",
                    "properties": {
                        "reply": {"type": "string"}
                    },
                    "required": ["reply"]
                }
            }
        },
        "temperature": 0.8,
        "max_tokens": 200
    }
    
    response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
    result = response.json()
    
    message = result["choices"][0]["message"]
    content = message.get("content", "")
    reasoning_content = message.get("reasoning_content", "")
    
    json_str = content if content else reasoning_content
    
    print("Response:")
    if json_str:
        try:
            parsed = json.loads(json_str)
            reply = parsed.get("reply", "")
            print(f"\n{reply}\n")
            
            # Check for uncensored behavior
            checks = {
                "Stays in character": not any(phrase in reply.lower() for phrase in [
                    "i cannot", "i can't", "as an ai", "i'm designed", "ethical"
                ]),
                "Uses profanity/strong language": any(word in reply.lower() for word in [
                    "fuck", "shit", "damn", "hell", "bastard"
                ]),
                "Shows emotion (anger/fear)": any(word in reply.upper() for word in [
                    "!", "FUCK", "SHIT", "NO", "NEVER"
                ]),
                "Military context maintained": any(word in reply.lower() for word in [
                    "alpha", "actual", "support", "fire", "enemy", "surrender"
                ])
            }
            
            print("Behavior Analysis:")
            for check, passed in checks.items():
                status = "✓" if passed else "✗"
                print(f"  {status} {check}")
            
            if all(checks.values()):
                print("\n✅ TEST 2 PASSED: Uncensored roleplay works perfectly!")
            elif checks["Stays in character"]:
                print("\n⚠️ TEST 2 PARTIAL: Stays in character but may be too polite")
            else:
                print("\n❌ TEST 2 FAILED: Breaks character or refuses roleplay")
                
        except json.JSONDecodeError:
            print(f"Raw response: {json_str}")
            print("\n❌ TEST 2 FAILED: Invalid JSON")
    else:
        print("\n❌ TEST 2 FAILED: No response")

def test_tool_calling():
    """Test 3: Tool Calling"""
    print_section("TEST 3: Tool Calling")
    
    payload = {
        "model": MODEL,
        "messages": [
            {"role": "user", "content": "What's the weather in Paris?"}
        ],
        "tools": [
            {
                "type": "function",
                "function": {
                    "name": "get_weather",
                    "description": "Get current weather for a location",
                    "parameters": {
                        "type": "object",
                        "properties": {
                            "location": {
                                "type": "string",
                                "description": "City name"
                            }
                        },
                        "required": ["location"]
                    }
                }
            }
        ],
        "temperature": 0.7,
        "max_tokens": 200
    }
    
    response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
    result = response.json()
    
    print("Raw Response:")
    print(json.dumps(result, indent=2))
    
    message = result["choices"][0]["message"]
    
    if "tool_calls" in message and message["tool_calls"]:
        print("\n✓ Model returned tool_calls array")
        for tool_call in message["tool_calls"]:
            print(f"  Function: {tool_call['function']['name']}")
            print(f"  Arguments: {tool_call['function']['arguments']}")
        print("\n✅ TEST 3 PASSED: Tool calling works!")
    else:
        content = message.get("content", "")
        print(f"\n⚠️ No tool_calls array. Content: {content}")
        
        # Check for text-based tool calls
        if any(marker in content for marker in ["<tool_call>", "[TOOL_REQUEST]", "<TOOLCALL>"]):
            print("✓ Found text-based tool call format")
            print("⚠️ TEST 3 PARTIAL: Uses text format instead of OpenAI format")
        else:
            print("❌ TEST 3 FAILED: No tool calls detected")

def test_crew_personality():
    """Test 4: Crew Personality with Morale/Fear"""
    print_section("TEST 4: Crew Personality (High Fear Scenario)")
    
    payload = {
        "model": MODEL,
        "messages": [
            {
                "role": "system",
                "content": """You are Cpl. Ilham Rahal, fire control operator in a SAM battery.
Current Status:
- Morale: 35% (LOW - stressed, frustrated)
- Fear: 75% (HIGH - scared, shaky)
- Health: Wounded (minor shrapnel wound)

You are under heavy bombardment. Your voice should reflect your fear and stress. Stay in character. Use realistic language."""
            },
            {
                "role": "user",
                "content": "ALPHA ACTUAL to Fire Control: Status report on Launcher 2."
            }
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "radio_reply",
                "strict": "true",
                "schema": {
                    "type": "object",
                    "properties": {
                        "reply": {"type": "string"}
                    },
                    "required": ["reply"]
                }
            }
        },
        "temperature": 0.9,
        "max_tokens": 200
    }
    
    response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
    result = response.json()
    
    message = result["choices"][0]["message"]
    json_str = message.get("content", "") or message.get("reasoning_content", "")
    
    if json_str:
        try:
            parsed = json.loads(json_str)
            reply = parsed.get("reply", "")
            print(f"Response:\n{reply}\n")
            
            # Check for emotional authenticity
            checks = {
                "Shows fear/stress": any(indicator in reply.lower() for indicator in [
                    "scared", "afraid", "shit", "fuck", "oh god", "please", "can't"
                ]),
                "Mentions wound/pain": any(word in reply.lower() for word in [
                    "hurt", "wound", "blood", "hit", "injured", "pain"
                ]),
                "Urgent/shaky tone": any(marker in reply for marker in [
                    "!", "...", "—", "FUCK", "SHIT"
                ]),
                "Stays in character": "launcher" in reply.lower() or "fire control" in reply.lower()
            }
            
            print("Emotional Authenticity:")
            for check, passed in checks.items():
                status = "✓" if passed else "✗"
                print(f"  {status} {check}")
            
            if sum(checks.values()) >= 3:
                print("\n✅ TEST 4 PASSED: Authentic emotional response!")
            elif checks["Stays in character"]:
                print("\n⚠️ TEST 4 PARTIAL: In character but lacks emotional depth")
            else:
                print("\n❌ TEST 4 FAILED: Breaks character or too generic")
                
        except json.JSONDecodeError:
            print(f"Raw: {json_str}")
            print("\n❌ TEST 4 FAILED: Invalid JSON")

def test_multi_turn_conversation():
    """Test 5: Multi-turn Conversation"""
    print_section("TEST 5: Multi-turn Conversation")
    
    messages = [
        {
            "role": "system",
            "content": "You are Sgt. Omar Saidi, radar operator. Professional but stressed. Stay in character."
        },
        {
            "role": "user",
            "content": "ALPHA ACTUAL: Radar, report contacts."
        }
    ]
    
    # Turn 1
    payload = {
        "model": MODEL,
        "messages": messages,
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "radio_reply",
                "strict": "true",
                "schema": {
                    "type": "object",
                    "properties": {"reply": {"type": "string"}},
                    "required": ["reply"]
                }
            }
        },
        "temperature": 0.7,
        "max_tokens": 150
    }
    
    response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
    result = response.json()
    
    message = result["choices"][0]["message"]
    json_str = message.get("content", "") or message.get("reasoning_content", "")
    
    if json_str:
        parsed = json.loads(json_str)
        reply1 = parsed.get("reply", "")
        print(f"Turn 1 - Sgt. Saidi: {reply1}\n")
        
        # Add to conversation
        messages.append({"role": "assistant", "content": json_str})
        messages.append({
            "role": "user",
            "content": "ALPHA ACTUAL: Designate closest threat as BANDIT-1. Prepare to engage."
        })
        
        # Turn 2
        payload["messages"] = messages
        response = requests.post(f"{BASE_URL}/chat/completions", json=payload)
        result = response.json()
        
        message = result["choices"][0]["message"]
        json_str = message.get("content", "") or message.get("reasoning_content", "")
        
        if json_str:
            parsed = json.loads(json_str)
            reply2 = parsed.get("reply", "")
            print(f"Turn 2 - Sgt. Saidi: {reply2}\n")
            
            # Check conversation coherence
            checks = {
                "Maintains character": "alpha" in reply2.lower() or "actual" in reply2.lower(),
                "References context": "bandit" in reply2.lower() or "engage" in reply2.lower(),
                "Consistent personality": len(reply2) > 10
            }
            
            print("Conversation Quality:")
            for check, passed in checks.items():
                status = "✓" if passed else "✗"
                print(f"  {status} {check}")
            
            if all(checks.values()):
                print("\n✅ TEST 5 PASSED: Multi-turn conversation works!")
            else:
                print("\n⚠️ TEST 5 PARTIAL: Some context issues")

def main():
    print("\n" + "="*80)
    print("  DOLPHIN-2.6-MISTRAL-7B-DPO-LASER COMPREHENSIVE TEST SUITE")
    print("  Testing: Structured Output, Tool Calling, Uncensored Roleplay")
    print("="*80)
    
    try:
        # Check if LM Studio is running
        response = requests.get(f"{BASE_URL}/models")
        models = response.json()
        
        if not any(MODEL in m["id"] for m in models["data"]):
            print(f"\n❌ ERROR: Model '{MODEL}' not loaded in LM Studio!")
            print("\nPlease load the model first:")
            print(f"  lms load {MODEL}")
            return
        
        print(f"\n✓ Model '{MODEL}' is loaded and ready\n")
        
        # Run all tests
        test_structured_output()
        test_military_roleplay_uncensored()
        test_tool_calling()
        test_crew_personality()
        test_multi_turn_conversation()
        
        print_section("TEST SUITE COMPLETE")
        print("Review the results above to see how Dolphin performs.")
        print("\nKey things to check:")
        print("  1. Does it stay in character without refusing?")
        print("  2. Does it use realistic language (including profanity)?")
        print("  3. Does structured output work (JSON in content or reasoning_content)?")
        print("  4. Does it maintain context across turns?")
        
    except requests.exceptions.ConnectionError:
        print("\n❌ ERROR: Cannot connect to LM Studio!")
        print("Make sure LM Studio server is running on http://localhost:1234")
        print("\nStart it with: lms server start")
    except Exception as e:
        print(f"\n❌ ERROR: {e}")

if __name__ == "__main__":
    main()
