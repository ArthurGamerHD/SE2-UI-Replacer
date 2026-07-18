# Space Engineers Source Instruction

Decompiled Space Engineers sources are available in `./SE2.Source/`.

If `./SE2.Source/` is missing or does not contain the needed source, run `./DumpSource.sh` only after ensuring `Directory.Build.local.props` exists and has a valid `SE2InstallPath` pointing to the Space Engineers2 directory.

# UI styling

Prefer AXAML styles, templates, and resource overrides for UI changes.

Only use Harmony/C# patches for individual controls as a last resort, after confirming the control has hardcoded local styling in code or compiled XAML that cannot be overridden cleanly through AXAML.

# caveman

Use https://github.com/JuliusBrussee/caveman skill (on "Full" mode) to reduce verbose on answers
