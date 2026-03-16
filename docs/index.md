---
_layout: landing
---
<div class="landing-hero">
<h1>BulkSharp</h1>
<p class="tagline">Production-grade .NET 8 library for defining, executing, and tracking bulk data operations from CSV and JSON files.</p>
<div class="cta-group">
<a href="getting-started/quick-start.md" class="landing-btn landing-btn-primary">
<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
Get Started
</a>
<a href="https://github.com/kalfonh/BulkSharp" class="landing-btn landing-btn-outline">
<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/></svg>
View on GitHub
</a>
</div>
</div>
<div class="landing-install">
<span class="prompt">$</span> <code>dotnet add package BulkSharp</code>
</div>
<div class="landing-section">
<h2>Features</h2>
<p class="section-subtitle">Everything you need for reliable bulk data processing at scale</p>
<div class="feature-grid">
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
</div>
<h3>Typed Operations</h3>
<p>Define metadata and row types with full validation. Each row is validated and processed individually with detailed error tracking.</p>
</div>
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="16 3 21 3 21 8"/><line x1="4" y1="20" x2="21" y2="3"/><polyline points="21 16 21 21 16 21"/><line x1="15" y1="15" x2="21" y2="21"/><line x1="4" y1="4" x2="9" y2="9"/></svg>
</div>
<h3>Step Pipelines</h3>
<p>Break complex processing into ordered steps with per-step retry and exponential backoff. Supports sync, polling, and signal-based async completion.</p>
</div>
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V9z"/><polyline points="13 2 13 9 20 9"/></svg>
</div>
<h3>CSV and JSON</h3>
<p>Stream-based parsing via IAsyncEnumerable for memory-efficient processing of large files. Format detected automatically by extension.</p>
</div>
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>
</div>
<h3>Pluggable Storage</h3>
<p>File system, in-memory, Amazon S3, or custom providers. Metadata persists via Entity Framework or in-memory stores.</p>
</div>
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
</div>
<h3>Background Scheduling</h3>
<p>Channels-based async scheduler with configurable worker count and backpressure. Immediate mode for testing. Custom schedulers supported.</p>
</div>
<div class="feature-card">
<div class="feature-icon">
<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>
</div>
<h3>Blazor Dashboard</h3>
<p>Drop-in monitoring UI with operation list, progress tracking, error details, per-row step drill-down, file upload, and REST API.</p>
</div>
</div>
</div>
<div class="landing-section-alt">
<div class="landing-section">
<h2>Define and Run</h2>
<p class="section-subtitle">A bulk operation is a class with an attribute. BulkSharp handles the rest.</p>
<div class="code-showcase">
<div class="code-showcase-header">Operation Definition</div>
<pre><code class="lang-csharp">[BulkOperation("import-users")]
public class UserImport : IBulkRowOperation&lt;UserMetadata, UserRow&gt;
{
    public Task ValidateMetadataAsync(UserMetadata meta, CancellationToken ct)
        =&gt; Task.CompletedTask;
    public Task ValidateRowAsync(UserRow row, UserMetadata meta, CancellationToken ct)
    {
        if (!row.Email.Contains('@'))
            throw new BulkValidationException("Invalid email");
        return Task.CompletedTask;
    }
    public Task ProcessRowAsync(UserRow row, UserMetadata meta, CancellationToken ct)
    {
        // Your business logic here
        return Task.CompletedTask;
    }
}</code></pre>
</div>
<div class="code-showcase">
<div class="code-showcase-header">Host Configuration</div>
<pre><code class="lang-csharp">services.AddBulkSharp(builder =&gt; builder
    .UseFileStorage(fs =&gt; fs.UseFileSystem())
    .UseMetadataStorage(ms =&gt; ms.UseSqlServer(opts =&gt;
        opts.ConnectionString = connectionString))
    .UseScheduler(s =&gt; s.UseChannels(opts =&gt;
        opts.WorkerCount = 4)));</code></pre>
</div>
</div>
</div>
<div class="landing-section">
<h2>Packages</h2>
<p class="section-subtitle">Most consumers only need the meta-package. Add optional packages as needed.</p>
<div class="package-grid">
<div class="package-card">
<span class="package-name">BulkSharp</span>
<p>Meta-package with DI registration, builders, and sensible defaults</p>
</div>
<div class="package-card">
<span class="package-name">BulkSharp.Core</span>
<p>Abstractions, domain models, attributes, and configuration</p>
</div>
<div class="package-card">
<span class="package-name">BulkSharp.Processing</span>
<p>Processing engine, data formats, storage implementations, and scheduling</p>
</div>
<div class="package-card">
<span class="package-name">BulkSharp.Dashboard</span>
<p>Blazor Server monitoring UI with REST API endpoints</p>
</div>
<div class="package-card">
<span class="package-name">BulkSharp.Data.EntityFramework</span>
<p>SQL Server persistence for operations and row records via EF Core</p>
</div>
<div class="package-card">
<span class="package-name">BulkSharp.Files.S3</span>
<p>Amazon S3 (and S3-compatible) file storage provider</p>
</div>
</div>
</div>
<div class="landing-cta">
<h2>Ready to get started?</h2>
<div class="landing-cta-links">
<a href="getting-started/quick-start.md">Quick Start Guide</a>
<a href="guides/architecture.md">Architecture Overview</a>
<a href="api/index.md">API Reference</a>
</div>
</div>
