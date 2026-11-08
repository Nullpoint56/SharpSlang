using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;


namespace SharpSlang.Native;

/// <summary>Argument used for specialization to types/values.</summary>
public unsafe partial struct SpecializationArg
{
    /// <summary>The kind of specialization argument.</summary>
    [NativeTypeName("slang::SpecializationArg::Kind")]
    public SpecializationArg.Kind kind;

    [NativeTypeName("__AnonymousRecord_slang_L5931_C5")]
    public _Anonymous_e__Union Anonymous;

    [UnscopedRef]
    public ref TypeReflection* type
    {
        get
        {
            return ref Anonymous.type;
        }
    }

    [UnscopedRef]
    public ref sbyte* expr
    {
        get
        {
            return ref Anonymous.expr;
        }
    }

    [return: NativeTypeName("slang::SpecializationArg")]
    public static SpecializationArg fromType([NativeTypeName("slang::TypeReflection *")] TypeReflection* inType)
    {
        SpecializationArg rs = new SpecializationArg();

        rs.kind = SpecializationArg.Kind.Type;
        rs.Anonymous.type = inType;
        return rs;
    }

    [return: NativeTypeName("slang::SpecializationArg")]
    public static SpecializationArg fromExpr([NativeTypeName("const char *")] sbyte* inExpr)
    {
        SpecializationArg rs = new SpecializationArg();

        rs.kind = SpecializationArg.Kind.Expr;
        rs.Anonymous.expr = inExpr;
        return rs;
    }

    [NativeTypeName("int32_t")]
    public enum Kind
    {
        /// <summary>An invalid specialization argument.</summary>
        Unknown = 0,
        /// <summary>Specialize to a type.</summary>
        Type = 1,
        /// <summary>An expression representing a type or value</summary>
        Expr = 2,
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe partial struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        [NativeTypeName("slang::TypeReflection *")]
        public TypeReflection* type;

        [FieldOffset(0)]
        [NativeTypeName("const char *")]
        public sbyte* expr;
    }
}
