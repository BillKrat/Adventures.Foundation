namespace Adventures.Ioc.Interfaces
{
    /// <summary>
    /// Keeps a type out of the lifetime scan. For a class the host wires by hand (a decorator used by one screen): if it also implemented a scanned
    /// interface, that interface would have two implementations and lose its unkeyed registration for everybody.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ExcludeFromLifetimeScanAttribute : Attribute
    {
    }
}
