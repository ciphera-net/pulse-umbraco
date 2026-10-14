using Ciphera.Pulse.Umbraco;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Web;
using Xunit;

namespace Ciphera.Pulse.Umbraco.Tests;

/// <summary>
/// This is the one place in this package where "the render array looked
/// right" and "the bytes the browser sees are right" are the same claim —
/// a TagHelper has no separate rendering layer in between, unlike Drupal
/// (where that gap needed a whole second, kernel-level test class). These
/// tests are the closest thing to <c>TagRenderTest</c> that does not need a
/// real Umbraco site; <c>scripts/verify.sh</c> is still the one that proves
/// it against an actual install.
/// </summary>
public class PulseAnalyticsTagHelperTests
{
    private const string FrontEndPath = "/some/page";
    private const string BackOfficePath = "/umbraco/some/path";

    [Fact]
    public void Renders_the_tag_on_a_front_end_request()
    {
        var sut = CreateSut(new PulseAnalyticsOptions { Domain = "example.com" }, inPreview: false);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.Equal(
            "<script defer data-domain=\"example.com\" src=\"https://js.ciphera.net/script.js\"></script>",
            output.Content.GetContent());
    }

    [Fact]
    public void The_pulse_analytics_element_itself_never_reaches_the_response()
    {
        var sut = CreateSut(new PulseAnalyticsOptions { Domain = "example.com" }, inPreview: false);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.Null(output.TagName);
    }

    [Fact]
    public void Suppresses_output_under_the_backoffice_path()
    {
        var sut = CreateSut(
            new PulseAnalyticsOptions { Domain = "example.com" },
            inPreview: false,
            requestPath: BackOfficePath);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.Equal(string.Empty, output.Content.GetContent());
    }

    [Fact]
    public void A_preview_session_is_excluded_by_default()
    {
        var sut = CreateSut(new PulseAnalyticsOptions { Domain = "example.com", TrackPreview = false }, inPreview: true);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.Equal(string.Empty, output.Content.GetContent());
    }

    [Fact]
    public void TrackPreview_true_renders_during_a_preview_session()
    {
        var sut = CreateSut(new PulseAnalyticsOptions { Domain = "example.com", TrackPreview = true }, inPreview: true);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.Contains("data-domain=\"example.com\"", output.Content.GetContent());
    }

    [Fact]
    public void A_front_end_request_outside_preview_is_unaffected_by_TrackPreview_being_off()
    {
        var sut = CreateSut(new PulseAnalyticsOptions { Domain = "example.com", TrackPreview = false }, inPreview: false);
        var output = CreateOutput();

        sut.Process(CreateContext(), output);

        Assert.NotEqual(string.Empty, output.Content.GetContent());
    }

    private static PulseAnalyticsTagHelper CreateSut(
        PulseAnalyticsOptions options,
        bool inPreview,
        string requestPath = FrontEndPath)
    {
        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.InPreviewMode.Returns(inPreview);

        var umbracoContextAccessor = Substitute.For<IUmbracoContextAccessor>();
        umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>()).Returns(callInfo =>
        {
            callInfo[0] = umbracoContext;
            return true;
        });

        // GetBackOfficePath() (Umbraco.Cms.Core.Hosting.HostingEnvironmentExtensions)
        // is, as of Umbraco 17.0.0, confirmed by decompiling the real NuGet
        // binary: `hostingEnvironment.ToAbsolute("~/umbraco")`, cached in a
        // static field for the process lifetime. GlobalSettings has no
        // UmbracoPath property any more, so there is nothing configurable to
        // feed it — this stub's ToAbsolute mirrors that exact transform
        // ("~/x" -> "/x"). scripts/verify.sh against a real install is the
        // one that proves the end-to-end behaviour; see RELEASING.md.
        var hostingEnvironment = Substitute.For<IHostingEnvironment>();
        hostingEnvironment.ToAbsolute(Arg.Any<string>())
            .Returns(callInfo => "/" + ((string)callInfo[0]).TrimStart('~', '/'));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = requestPath;

        return new PulseAnalyticsTagHelper(
            new FakeOptionsMonitor<PulseAnalyticsOptions>(options),
            umbracoContextAccessor,
            hostingEnvironment)
        {
            ViewContext = new ViewContext { HttpContext = httpContext },
        };
    }

    private static TagHelperOutput CreateOutput() =>
        new(
            "pulse-analytics",
            new TagHelperAttributeList(),
            (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static TagHelperContext CreateContext() =>
        new(new TagHelperAttributeList(), new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));

    /// <summary>A minimal, hand-written <see cref="IOptionsMonitor{T}"/> — no need for NSubstitute on three members.</summary>
    private sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public FakeOptionsMonitor(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
