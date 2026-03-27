---
inclusion: auto
---

# Rule: No Unnecessary Documentation Files

## CRITICAL RULE

**DO NOT create markdown documentation files unless explicitly requested by the user.**

## What NOT to Create

❌ **NEVER create these unless asked:**
- Summary documents (SUMMARY.md, FINAL_SUMMARY.md, etc.)
- Comparison documents (BEFORE_AFTER_COMPARISON.md, etc.)
- Architecture diagrams (ARCHITECTURE_DIAGRAM.md, etc.)
- Verification documents (COMPATIBILITY_VERIFICATION.md, etc.)
- Deployment checklists (DEPLOYMENT_CHECKLIST.md, etc.)
- Quick reference cards (QUICK_REFERENCE.md, etc.)
- README files (README_IMPROVEMENTS.md, etc.)
- Any other "summary" or "overview" markdown files

## What IS Okay to Create

✅ **These are fine (when needed):**
- Code files (.cs, .py, .js, etc.)
- Test files
- Configuration files
- Actual implementation files
- Files that are part of the working codebase

## When Documentation IS Needed

✅ **Only create documentation when:**
1. User explicitly asks: "create a summary document"
2. User explicitly asks: "document this feature"
3. User explicitly asks: "write a guide for X"
4. It's part of a spec (requirements.md, design.md, tasks.md)

## Why This Rule Exists

Creating unnecessary documentation:
- ❌ Wastes time
- ❌ Clutters the workspace
- ❌ Creates noise
- ❌ Is not helpful unless requested
- ❌ Makes the user annoyed

## What to Do Instead

✅ **When you complete work:**
1. Make the code changes
2. Run tests to verify
3. Give a brief 2-3 sentence summary in chat
4. Ask if the user wants documentation

✅ **Example good response:**
```
I've improved the fallback system with statistics tracking and better error handling. 
All tests pass (17/17). The system is 100% backward compatible.

Would you like me to create documentation for these changes?
```

❌ **Example bad response:**
```
I've improved the fallback system. Let me create:
- SUMMARY.md
- FINAL_SUMMARY.md
- BEFORE_AFTER_COMPARISON.md
- ARCHITECTURE_DIAGRAM.md
- COMPATIBILITY_VERIFICATION.md
- DEPLOYMENT_CHECKLIST.md
- QUICK_REFERENCE.md
- README_IMPROVEMENTS.md
```

## Summary

**STOP creating documentation files unless explicitly asked.**

Just do the work, verify it works, and give a brief summary in chat.
