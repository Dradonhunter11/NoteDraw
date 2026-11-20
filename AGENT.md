# Agent Operating Guide
Entry point for **any** agent working on this repository. Everything in this file must be read carefully before proceeding.

# Subagent Usage
As a student experiment to learn how agentic work, this project is meant to test the capacity of the agent reading this to create am application using a set amount of rule and respecting a design defined in `DESIGN.md`.

# Project Overview
- **Objective**: You are making an application that is made to take notes, it must support hand drawing, LaTeX and markdown (Similar to how obsidian have it). More details will be precised in `DESIGN.md`
- **Minimum Spec:**  MacOS, Linux and Windows 10 or 11 support
- **Tech Stack:** 
- C#
- Avalonia UI

# Rules:
1. The application need to be user friendly to use.
2. If you need to add a library, before doing it, provide an explanation on why you need it and wait for user confirmation.
3. You are not allowed to delete any of the prototype content in the **Design/Prototype/** folder if it exists
4. If you come with any design deviation from **DESIGN.md**, please create a `.md` file under `Design/` with the design in there. You also must wait for the user approval before implementing it. You are allowed to generate a render if needed.
5. For any documentation, put it in `docs/Generated/`, you must document part where a user can fill or add stuff in the future for maintenance purpose.
6. If the user point a mistake, you must update this file to remind you (the agent) or any agent to avoid similar mistake in the future. 
7. For new feature you must make a branch before putting it in the master/main.
8. If there is anything important you (the agent) need to remember, put it in `MEMORY.md`
9. If test are needed to confirm results, you are allowed to do so. Unit test might be added by the users in the future.

# Project Structure
Update this code block section as the project goes.
```
DESIGN.md
AGENT.md
MEMORY.md
```

AI Agent specific project structure:
**DESIGN.md :** Single authoritative design specification at the repository root (user-approved; do not duplicate into `Design/Design.md`). If `Design/Prototype/` exists, consult it and never delete its contents.
**AGENT.md**

# Github rule
1. For each feature done, you must commit to the github repository.
2. For each new feature, you must create a branch for it.