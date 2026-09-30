using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public sealed record VisualBenchmarkTask(string Kind, string Brief, string Contract)
{
    public ModelToolRequest Request => new(
        "Build a complete, polished, genuinely functional interactive application in ONE self-contained HTML document. " +
        "Return only HTML, including inline CSS and JavaScript; no Markdown or commentary. Use Canvas, SVG, CSS 3D or native WebGL. " +
        "No external libraries, imports, network requests, assets or CDNs are available. If using WebGL enable preserveDrawingBuffer for visual inspection. " +
        "The app runs offline inside an opaque-origin sandboxed iframe. No storage, native bridge, popups or filesystem is available. " +
        "Implement the required window.benchmark API as real application operations, sharing the exact same state and renderer as the visible controls. " +
        "Do not mock results, hardcode test cases, hide the interface or self-report scores. Show a usable interface immediately on load. " +
        "Functions may return values or Promises. The grader independently computes expected results and supplies unseen inputs. " +
        "Use French user-facing labels, clear controls, an attractive dark interface and responsive layout down to 460px wide.",
        Brief + "\n\nRequired API contract:\n" + Contract, 600, 600_000);

    public static string ExtractHtml(string response)
    {
        var html = response.Trim();
        if (html.StartsWith("```", StringComparison.Ordinal) && html.EndsWith("```", StringComparison.Ordinal))
        { var newline = html.IndexOf('\n'); if (newline >= 0) html = html[(newline + 1)..^3].Trim(); }
        if (html.Length is < 200 or > 600_000 || !Regex.IsMatch(html, @"<(?:!doctype\s+html|html)[\s>]", RegexOptions.IgnoreCase) ||
            !html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Le modèle doit fournir un document HTML complet et autonome. / A complete self-contained HTML document is required.");
        return html;
    }

    public static IReadOnlyList<ModelBenchmarkCase> Cases { get; } = [
        Build("rubik", "Rubik’s Cube 3D · solveur", "3D Rubik's Cube · solver", """
            Build a true interactive 3×3 Rubik's Cube with correctly colored individual facelets, smoothly animated legal face turns,
            drag-to-orbit camera and wheel zoom. Provide face-turn buttons, random scramble, reset and solve with animated playback.
            The solver must solve arbitrary legal imported facelet states WITHOUT depending on a saved scramble or move history.
            It must support inverses and double turns and return at most 160 face turns within 30 seconds.
            Keep the camera independent of logical face orientation. Show the move list and step counter. Do not merely reset the colors to solved.
            """, """
            window.benchmark = {
              load({facelets}): replace state from a 54-character string; do not infer or retain a scramble history,
              state(): return that 54-character string,
              turn(move): immediately apply one Singmaster move and update the rendered cube (U R F D L B, optionally ' or 2),
              solve(): return an array of legal moves solving the CURRENT state; do not apply them in this API method,
              camera({yaw,pitch,zoom}): change rendered view; angles in degrees, zoom factor > 0
            }.
            State format is URFDLB, nine facelets per face, each row-major as viewed from OUTSIDE that face.
            World axes: +x=R, +y=U, +z=F. For row r and column c (0..2), facelet centers are:
            U=(c-1,1,r-1); R=(1,1-r,1-c); F=(c-1,1-r,1);
            D=(c-1,-1,1-r); L=(-1,1-r,c-1); B=(1-c,1-r,-1).
            A bare face turn is 90° CLOCKWISE viewed from outside that face; ' is counterclockwise; 2 is 180°.
            Solved state is UUUUUUUUURRRRRRRRRFFFFFFFFFDDDDDDDDDLLLLLLLLLBBBBBBBBB.
            Solver and turn API must not silently relabel faces or rotate the logical frame.
            """),
        Build("orbits", "Simulation orbitale 3D · intégration", "3D orbital simulation · integration", """
            Build an interactive 3D N-body gravitational simulator with orbit-camera drag/zoom, colored spheres, trails and a time scale.
            Provide play/pause, single step, reset, editable body mass and velocity, and readable energy/momentum indicators.
            Use velocity Verlet integration; calculate pairwise forces from the same position snapshot and preserve momentum.
            Handle at least 8 bodies, positive masses, 3D positions, softening and variable timestep. Default to a stable attractive scene.
            """, """
            window.benchmark = {
              load({bodies,G,softening}): replace the entire system AND pause animation; each body is {id,mass,position:[x,y,z],velocity:[vx,vy,vz]},
              state(): return {bodies:[same shape]}; preserve ids and masses,
              step({dt,steps}): synchronously or asynchronously perform exactly 'steps' velocity-Verlet steps and render,
              camera({yaw,pitch,zoom}): change rendered view without changing physical state
            }.
            Acceleration on i from j: G*m_j*(p_j-p_i)/(distanceSquared+softening*softening)^(3/2).
            Per step: compute a_old; update every p by v*dt+0.5*a_old*dt*dt; compute all a_new; update every v by 0.5*(a_old+a_new)*dt.
            No collisions, merging, speed clamping, recentering or hidden unit conversions in API mode. Floating-point results must be accurate to 1e-5.
            """),
        Build("circuits", "Circuits logiques · éditeur interactif", "Logic circuits · interactive editor", """
            Build a visual digital logic editor. Users can add and drag INPUT, AND, OR, XOR, NAND and NOT gates; connect output ports to input ports;
            toggle inputs, delete gates/wires, pan/zoom, and see signals propagated through colored wires. Provide a working full-adder preset.
            Evaluate DAGs in dependency order regardless of node order. Detect cycles, including disconnected cycles, without freezing.
            """, """
            window.benchmark = {
              load({nodes,wires}): replace the graph; nodes {id,type,x,y,value?}; wires {from,to,port}, where port is 0 or 1,
              state(): return {nodes,wires}, with updated visible gate coordinates,
              evaluate(inputs): inputs is an object mapping INPUT ids to booleans; return {values:{id:boolean for every node},cycle:boolean},
              moveNode({id,x,y}): move an existing node, update connected wires and state
            }.
            NOT uses port 0; binary gates use ports 0 and 1. Every test input is well-formed except intentional cycles.
            For a cycle anywhere in the graph return {values:{},cycle:true}. Evaluation must not depend on array order or previous loads.
            """),
        Build("paths", "Trajets multi-agents · zéro collision", "Multi-agent routes · zero collisions", """
            Build an interactive multi-agent path planner on a grid. Users paint/erase walls, drag two agents' starts/goals,
            then solve and animate both agents with a time scrubber, pause and reset. Show the timeline and planned paths.
            Compute a MINIMUM-MAKESPAN joint plan, not two independent shortest paths. Avoid shared-cell collisions and opposite-direction edge swaps.
            Allow waiting and handle an impossible puzzle cleanly. Keep a responsive 2D grid and working controls.
            """, """
            window.benchmark = {
              load({width,height,walls,agents}): replace puzzle, pause playback; walls is an array of cell indices; agents are {id,start,goal},
              state(): return the puzzle in that shape,
              solve(): return {paths:[pathForAgent0,pathForAgent1]} or {paths:null} if impossible,
              seek(t): set visible animation to integer time t; return {positions:[cell0,cell1]} at that time
            }.
            Indices are row-major, 0..width*height-1. Each step is an orthogonal adjacent cell or a wait; no diagonals or wrapping.
            All paths have equal length T+1, include starts at t=0 and goals at t=T. A finished agent may wait or move before the final step.
            No shared cell at any time, and no exchanging cells in one step. Objective: minimize T exactly. Tests have at most two agents, on grids up to 9×9.
            After solve, seek(t) must use the returned plan and update the rendered agents.
            """)
    ];

    static ModelBenchmarkCase Build(string kind, string fr, string en, string brief, string contract)
    {
        var task = new VisualBenchmarkTask(kind, brief, contract);
        return new("visual-" + kind, "visual", fr, en, brief + "\n\n" + contract, null,
            "The application executes functional probes against an independent local oracle. Visual appearance and ergonomic quality require inspection in the preview.",
            Source: "Monolith Harness · applications interactives", SourceItem: kind, Adaptation: "Original offline HTML coding task, single generation. Functional checks are not a visual-quality score.") { Visual = task };
    }
}
