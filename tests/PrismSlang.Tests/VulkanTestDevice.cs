// A headless Vulkan device with the validation layers talking.
//
// The fourth oracle. The first three are the emitted module (§9.11), invariants with no oracle
// (§9.12), and the specification as read by a person (§9.14) — and that last one is the weak link.
// RequireDistinctLocations refuses a shader because of a rule this repository read out of the spec,
// in a case where Slang and the emitted module agree with each other and disagree with the reading.
// Nothing already here could settle it, because both sides of every comparison came from Slang.
//
// The validation layers are the party that decides. They are Khronos's own encoding of the rules a
// driver enforces, so building a real VkPipeline out of PrismSlang's projection and the SPIR-V it
// accompanies is as close as this repository gets to asking the specification directly.
//
// Headless: no surface, no swapchain, no window, and dynamic rendering instead of a render pass —
// which removes the last reason to need one.

using System.Runtime.InteropServices;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace PrismSlang.Tests;

/// <summary>One message the validation layers produced.</summary>
/// <param name="Severity">Error, warning, info or verbose.</param>
/// <param name="Id">The VUID or message identifier, where the layers gave one.</param>
/// <param name="Text">The full message.</param>
public sealed record ValidationMessage(
    VkDebugUtilsMessageSeverityFlagsEXT Severity,
    string Id,
    string Text)
{
    public bool IsError => (Severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0;
    public bool IsWarning => (Severity & VkDebugUtilsMessageSeverityFlagsEXT.Warning) != 0;

    public override string ToString() => $"[{(IsError ? "ERROR" : "warning")}] {Id}: {Text}";
}

/// <summary>
/// A Vulkan instance and device with <c>VK_LAYER_KHRONOS_validation</c> enabled, or a reason there
/// is not one.
/// </summary>
public sealed unsafe class VulkanTestDevice : IDisposable
{
    private const string ValidationLayer = "VK_LAYER_KHRONOS_validation";
    private const string DebugExtension = "VK_EXT_debug_utils";

    // The messenger is called from the loader, so the sink cannot be an instance field reached
    // through a captured `this`. Tests run serially — AssemblyInfo disables parallelisation — so
    // one process-wide sink is enough, and Capture owns clearing it.
    private static readonly List<ValidationMessage> Sink = [];
    private static readonly Lock SinkLock = new();

    private VkInstance _instance;
    private VkDebugUtilsMessengerEXT _messenger;
    private VkDevice _device;

    public VkInstanceApi InstanceApi { get; private set; } = null!;
    public VkDeviceApi Api { get; private set; } = null!;
    public VkPhysicalDevice PhysicalDevice { get; private set; }

    /// <summary>Why a validating device is unavailable, or null if one is.</summary>
    public static string? Unavailable => Probe.Value;

    /// <remarks>
    /// By trying, not by enumerating. The first version compared <c>VK_LAYER_KHRONOS_validation</c>
    /// against the names from <c>vkEnumerateInstanceLayerProperties</c> and decided the layer was
    /// absent on a machine that had it registered — reading a name out of a fixed-size buffer in a
    /// by-value struct is easy to get subtly wrong, and a probe that is wrong skips every test in
    /// the file with a confident and false explanation. Creating the instance is the same question
    /// asked of the loader, which cannot be mistaken about its own answer, and
    /// <c>ErrorLayerNotPresent</c> says exactly which case this is.
    /// </remarks>
    private static readonly Lazy<string?> Probe = new(() =>
    {
        try
        {
            if (vkInitialize() != VkResult.Success)
                return "no Vulkan loader on this machine. Install a Vulkan runtime or the SDK.";

            using var probe = new VulkanTestDevice();
            return null;
        }
        catch (VulkanUnavailableException e) when (e.Result == VkResult.ErrorLayerNotPresent)
        {
            return $"{ValidationLayer} is not installed. It ships with the Vulkan SDK: "
                 + "https://vulkan.lunarg.com/sdk/home";
        }
        catch (VulkanUnavailableException e) when (e.Result == VkResult.ErrorExtensionNotPresent)
        {
            return $"{DebugExtension} is not available, so the layers have nowhere to report to.";
        }
        catch (Exception e)
        {
            return $"no usable Vulkan device: {e.Message}";
        }
    });

    /// <summary>A Vulkan call that failed while standing the test device up.</summary>
    public sealed class VulkanUnavailableException(VkResult result, string what)
        : Exception($"{what}: {result}")
    {
        public VkResult Result { get; } = result;
    }

    /// <summary>
    /// Why a 64-bit vertex attribute cannot be tested on this device, or null if it can.
    /// </summary>
    /// <remarks>
    /// A separate gate because a driver may be perfectly usable and still not accept
    /// <c>VK_FORMAT_R64G64B64A64_SFLOAT</c> in a vertex buffer. Lavapipe is exactly that case —
    /// it reports <c>bufferFeatures 0</c> for the format — so a pipeline using one is rejected with
    /// <c>VUID-VkVertexInputAttributeDescription-format-00623</c> before anything gets as far as
    /// checking location assignment. Running the test there would assert on the wrong message, or
    /// worse, pass because the driver objected to something else entirely.
    /// </remarks>
    public static string? WideVertexFormatUnsupported => WideFormatProbe.Value;

    private static readonly Lazy<string?> WideFormatProbe = new(() =>
    {
        if (Unavailable is { } reason) return reason;

        using var device = new VulkanTestDevice();
        return device.SupportsVertexFormat(VkFormat.R64G64B64A64Sfloat)
            ? null
            : "this device does not accept VK_FORMAT_R64G64B64A64_SFLOAT as a vertex buffer format "
              + "(lavapipe does not), so a pipeline using one is rejected before location "
              + "assignment is ever checked.";
    });

    /// <summary>Whether the device accepts this format in a vertex buffer.</summary>
    public bool SupportsVertexFormat(VkFormat format)
    {
        var properties = new VkFormatProperties();
        InstanceApi.vkGetPhysicalDeviceFormatProperties(PhysicalDevice, format, &properties);
        return (properties.bufferFeatures & VkFormatFeatureFlags.VertexBuffer) != 0;
    }

    public VulkanTestDevice()
    {
        using var layers = new VkStringArray([ValidationLayer]);
        using var extensions = new VkStringArray([DebugExtension]);
        var appName = "PrismSlang.Tests"u8;

        try
        {
            fixed (byte* appNamePtr = appName)
            {
            var application = new VkApplicationInfo
            {
                pApplicationName = appNamePtr,
                apiVersion = VkVersion.Version_1_3,
            };

            var instanceInfo = new VkInstanceCreateInfo
            {
                pApplicationInfo = &application,
                enabledLayerCount = layers.Length,
                ppEnabledLayerNames = layers,
                enabledExtensionCount = extensions.Length,
                ppEnabledExtensionNames = extensions,
            };

            Check(vkCreateInstance(&instanceInfo, out _instance), "vkCreateInstance");
            InstanceApi = new VkInstanceApi(_instance);

            var messengerInfo = new VkDebugUtilsMessengerCreateInfoEXT
            {
                messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Error
                                | VkDebugUtilsMessageSeverityFlagsEXT.Warning,
                messageType = VkDebugUtilsMessageTypeFlagsEXT.Validation
                            | VkDebugUtilsMessageTypeFlagsEXT.General,
                pfnUserCallback = &OnMessage,
            };

            Check(InstanceApi.vkCreateDebugUtilsMessengerEXT(&messengerInfo, null, out _messenger),
                "vkCreateDebugUtilsMessengerEXT");

            PhysicalDevice = PickDevice();
            _device = CreateDevice(PhysicalDevice);
            Api = new VkDeviceApi(InstanceApi, _device);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private VkPhysicalDevice PickDevice()
    {
        uint count = 0;
        Check(InstanceApi.vkEnumeratePhysicalDevices(out count), "vkEnumeratePhysicalDevices");
        if (count == 0) throw new InvalidOperationException("no Vulkan physical devices");

        var devices = new VkPhysicalDevice[count];
        Check(InstanceApi.vkEnumeratePhysicalDevices(devices.AsSpan()), "vkEnumeratePhysicalDevices");

        // Any device will do; the layers do the work, not the hardware. A software ICD such as
        // lavapipe is equally valid and is what a CI runner would have.
        return devices[0];
    }

    private VkDevice CreateDevice(VkPhysicalDevice physical)
    {
        var priority = 1.0f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = 0,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        // Everything a corpus shader might declare, where the device has it. Missing one turns a
        // layout question into a feature-support question, which is not what is being asked.
        var vulkan12 = new VkPhysicalDeviceVulkan12Features
        {
            runtimeDescriptorArray = true,
            descriptorBindingPartiallyBound = true,
            descriptorBindingVariableDescriptorCount = true,
            shaderSampledImageArrayNonUniformIndexing = true,
        };
        var vulkan13 = new VkPhysicalDeviceVulkan13Features
        {
            dynamicRendering = true,
            pNext = &vulkan12,
        };

        var supported = new VkPhysicalDeviceFeatures();
        InstanceApi.vkGetPhysicalDeviceFeatures(physical, &supported);

        var features = new VkPhysicalDeviceFeatures
        {
            shaderFloat64 = supported.shaderFloat64,
            shaderInt64 = supported.shaderInt64,
            shaderStorageImageWriteWithoutFormat = supported.shaderStorageImageWriteWithoutFormat,
            shaderStorageImageReadWithoutFormat = supported.shaderStorageImageReadWithoutFormat,
            fragmentStoresAndAtomics = supported.fragmentStoresAndAtomics,
            vertexPipelineStoresAndAtomics = supported.vertexPipelineStoresAndAtomics,
            geometryShader = supported.geometryShader,
            tessellationShader = supported.tessellationShader,
        };

        var deviceInfo = new VkDeviceCreateInfo
        {
            pNext = &vulkan13,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            pEnabledFeatures = &features,
        };

        Check(InstanceApi.vkCreateDevice(physical, &deviceInfo, null, out VkDevice device),
            "vkCreateDevice");
        return device;
    }

    // ---------------------------------------------------------------- messages

    /// <summary>Starts collecting validation output; everything inside the scope lands in it.</summary>
    public static Capture Collect() => new();

    /// <summary>A scope over the messages the layers produce inside it.</summary>
    public sealed class Capture : IDisposable
    {
        public Capture()
        {
            lock (SinkLock) Sink.Clear();
        }

        /// <summary>Everything so far, in the order the layers reported it.</summary>
        public IReadOnlyList<ValidationMessage> Messages
        {
            get { lock (SinkLock) return [.. Sink]; }
        }

        public IReadOnlyList<ValidationMessage> Errors =>
            [.. Messages.Where(m => m.IsError)];

        /// <summary>Every message, one per line, for an assertion failure to print.</summary>
        public string Report() =>
            Messages.Count == 0
                ? "    (the layers said nothing)"
                : string.Join("", Messages.Select(m => $"    {m}\n"));

        public void Dispose()
        {
            lock (SinkLock) Sink.Clear();
        }
    }

    [UnmanagedCallersOnly]
    private static uint OnMessage(
        VkDebugUtilsMessageSeverityFlagsEXT severity,
        VkDebugUtilsMessageTypeFlagsEXT type,
        VkDebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        // Called from the loader. An exception escaping here is undefined behaviour, exactly as in
        // SlangFileSystem's thunks — §9.15.
        try
        {
            var id = data->pMessageIdName is null ? "" : ReadUtf8(data->pMessageIdName);
            var text = data->pMessage is null ? "" : ReadUtf8(data->pMessage);

            lock (SinkLock) Sink.Add(new ValidationMessage(severity, id, text));
        }
        catch
        {
            // Nothing useful to do, and nothing may be thrown.
        }

        return 0; // VK_FALSE: do not abort the call that produced the message.
    }

    private static string ReadUtf8(byte* p)
    {
        if (p is null) return "";
        var length = 0;
        while (p[length] != 0) length++;
        return Encoding.UTF8.GetString(p, length);
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success) throw new VulkanUnavailableException(result, what);
    }

    public void Dispose()
    {
        if (_device.IsNotNull)
        {
            Api?.vkDestroyDevice();
            _device = VkDevice.Null;
        }
        if (_messenger.IsNotNull)
        {
            InstanceApi.vkDestroyDebugUtilsMessengerEXT(_messenger, null);
            _messenger = VkDebugUtilsMessengerEXT.Null;
        }
        if (_instance.IsNotNull)
        {
            InstanceApi.vkDestroyInstance();
            _instance = VkInstance.Null;
        }
    }
}

/// <summary>A fact needing a Vulkan device with the validation layers; says why when there is none.</summary>
public sealed class VulkanValidationFactAttribute : FactAttribute
{
    public VulkanValidationFactAttribute()
    {
        if (!SlangNative.Available) Skip = SlangNative.SkipReason;
        else if (VulkanTestDevice.Unavailable is { } reason) Skip = reason;
    }
}

/// <summary>The theory counterpart.</summary>
public sealed class VulkanValidationTheoryAttribute : TheoryAttribute
{
    public VulkanValidationTheoryAttribute()
    {
        if (!SlangNative.Available) Skip = SlangNative.SkipReason;
        else if (VulkanTestDevice.Unavailable is { } reason) Skip = reason;
    }
}

/// <summary>
/// A fact that additionally needs a device accepting 64-bit vertex attribute formats.
/// </summary>
public sealed class Wide64VertexFactAttribute : FactAttribute
{
    public Wide64VertexFactAttribute()
    {
        if (!SlangNative.Available) Skip = SlangNative.SkipReason;
        else if (VulkanTestDevice.WideVertexFormatUnsupported is { } reason) Skip = reason;
    }
}
