# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2024-10-29
This update includes a whole slew of new UX improvements and the beginnings of some big compiler changes that will eventually allow for automatic-parallelization.

### General/UI
- Adds a new node inspector. This is a tab that can show debug values for the selected node.
- Adds a "Find References" to a node's right-click menu.
- Increase the size of the node search window by default.
- Add alt + middle mouse for panning the node view.
- Adds a striped background for nodes that have been lifted into nullability.
- Edges can be deleted with a 'knife' tool. 
- Adds drag&drop from the project window for Lattice Scripts, onto GameObjects.
- Adds a basic 'redirector' node.
- Edges can now be created by dragging on port errors.
- Updated icons to match Unity ECS style.
- Automatically bake a LocalTransform component if a LatticeGraph requires it.

### New Nodes
- Boolean 'Not' node
- An 'Animated Transform' node that will bake the selected AnimationClip's root motion into a BlobAnimationClip and return it within Lattice.
- 'SimpleAnimate' now has an optional 'OutputScale' parameter.
- Many new basic math nodes!

### Compiler
- Adds an optimization pass framework to the compiler.
- Adds an optimization pass that uses pointers for modifying node state during execution.
- Dead code elimination now uses new optimization pass system.
- Adds an optimization pass that removes 'Identity' nodes (aka. "Redirectors")
- Remove lingering boxing in code execution.
- Fixes correctness bug in nullable lifting of complex nodes with state. 
- Reworks how we compile barriers in the IR (ie. how to force nodes to execute after/before others).
- Multitudes of internal compiler tweaks.

### Bugfixes
- Fix guid stability for graphs during build.
- Add check that output tuple names must not match input parameter names.
- Many more tiny bug fixes and sanity checks.

## [0.4.0] - 2024-05-08
This is the first public version of Lattice.

### Compiler
- Implement support for optional and default C# function arguments.  
- Adds [LatticePhase] attribute to mark a node to execute at a specific point in the frame. 
- Emit ProfilerMarkers in generated assembly.
- Adds dead code elimination optimization pass. This removes known-pure nodes that are not used or debugged.
- Performance: Skip try/catch on node bodies that are known to not throw.
- Performance: Menu item to disable value debugging (Lattice/Options).
- Performance: Replace Sigil with Gremit, a wildly more performant IL emitter.
- New menu item to recompile all graphs in the project.

### General/UI
- Adds Jump-To-Definition in C# when double-clicking on a node.
- Allow multiple graphs on a single entity.
- Custom editor tooltip implementation that displays while in Play Mode.
- Multi-window support.
- Add icons to Entity and ComponentData nodes.
- "Repair Nodes" tool to aid refactoring if C# functions are missing.
- A button to open the IR representation in GraphViz.