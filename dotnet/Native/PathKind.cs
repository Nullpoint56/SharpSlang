namespace SharpSlang.Native;

/// <summary>Used to determine what kind of path is required from an input path</summary>
public enum PathKind
{
    /// <summary>Given a path, returns a simplified version of that path.</summary>
    /// <remarks>
    /// <para>This typically means removing '..' and/or '.' from the path.</para>
    /// <para>A simplified path must point to the same object as the original.</para>
    /// </remarks>
    Simplified = 0,
    /// <summary>Given a path, returns a 'canonical path' to the item.</summary>
    /// <remarks>
    /// <para>This may be the operating system 'canonical path' that is the unique path to the item.</para>
    /// <para>If the item exists the returned canonical path should always be usable to access the</para>
    /// <para>item.</para>
    /// <para>If the item the path specifies doesn't exist, the canonical path may not be returnable</para>
    /// <para>or be a path simplification.</para>
    /// <para>Not all file systems support canonical paths.</para>
    /// </remarks>
    Canonical = 1,
    /// <summary>Given a path returns a path such that it is suitable to be displayed to the user.</summary>
    /// <remarks>
    /// <para>For example if the file system is a zip file - it might include the path to the zip</para>
    /// <para>container as well as the path to the specific file.</para>
    /// <para>NOTE! The display path won't necessarily work on the file system to access the item</para>
    /// </remarks>
    Display = 2,
    /// <summary>Get the path to the item on the *operating system* file system, if available.</summary>
    OperatingSystem = 3,
    CountOf,
}
