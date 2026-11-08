namespace SharpSlang.Native;

public enum OperandDataType
{
    /// <summary>General data type, can be any type.</summary>
    General = 0,
    /// <summary>32-bit integer.</summary>
    Int32 = 1,
    /// <summary>64-bit integer.</summary>
    Int64 = 2,
    /// <summary>32-bit floating-point number.</summary>
    Float32 = 3,
    /// <summary>64-bit floating-point number.</summary>
    Float64 = 4,
    /// <summary>String data type, represented as a pointer to a null-terminated string.</summary>
    String = 5,
}
