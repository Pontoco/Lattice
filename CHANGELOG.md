# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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