# Documentation Maintenance Guidelines

## Purpose
This document defines **mandatory rules** for maintaining project documentation. As an AI assistant, you must follow these guidelines when creating, updating, or reviewing documentation to ensure consistency, completeness, and maintainability.

## Core Principles

### 1. Documentation is Code
- Treat documentation with the same rigor as source code
- Review and update documentation in the same commit as code changes
- Documentation changes require the same quality standards as code

### 2. Single Source of Truth
- Each piece of information exists in exactly one place
- Use references and links instead of duplicating content
- Update linked content when the source changes

### 3. Progressive Disclosure
- Start with essential information, layer in complexity
- Beginners should find quick starts easily
- Experts should find detailed technical information accessible

### 4. Documentation-Driven Development
- Update documentation BEFORE implementing changes
- Use documentation as specification
- Keep documentation synchronized with implementation

### 5. Centralized Documentation Storage
- **ALL project documentation MUST be stored in the `/docs` folder**
- Code-specific documentation (like implementation notes) should be in the code directory
- Before creating new documentation, ALWAYS review the existing `/docs` folder structure
- Update existing documents instead of creating duplicates

### 6. Documentation Size and Readability
- Maximum document size: **~1000 lines** for optimal readability
- If a document exceeds this limit, split it into logical sections
- Use clear naming for multi-part documents: `topic-part-1.md`, `topic-part-2.md`
- Each part should be self-contained with proper context and cross-references

## Quick Reference: Documentation Workflow

**Every time you need to create or update documentation:**

```
┌─────────────────────────────────────────────────────────┐
│ STEP 1: Review Existing /docs Folder                   │
│ - Use file_search or list_dir to explore structure     │
│ - Identify existing related documents                  │
│ - Check for duplicate content                          │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ STEP 2: Decide Update vs New                           │
│ - Update existing if content is related                │
│ - Create new only if truly distinct topic              │
│ - Check current document size before updating          │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ STEP 3: Verify Location                                │
│ - /docs/architecture/ → Design & decisions             │
│ - /docs/guides/ → How-to & tutorials                   │
│ - /docs/api/ → API reference                           │
│ - /docs/getting-started/ → Onboarding                  │
│ - Code directory → Temporary only, plan migration      │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ STEP 4: Check Size & Split if Needed                   │
│ - Target: < 1000 lines per document                    │
│ - If over limit: Create {topic}-part-{N}.md            │
│ - Add navigation links between parts                   │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ STEP 5: Update & Cross-Reference                       │
│ - Write/update content                                 │
│ - Add links from related documents                     │
│ - Update CHANGELOG.md                                  │
│ - Update main README.md if needed                      │
└─────────────────────────────────────────────────────────┘
```

**CRITICAL RULES:**
- ✅ ALL documentation in /docs folder (except temporary code notes)
- ✅ Review existing docs BEFORE creating new ones
- ✅ Maximum ~1000 lines per document
- ✅ Split large documents into logical parts
- ✅ Update existing docs instead of duplicating
- ❌ NEVER create duplicate content
- ❌ NEVER skip the review process
- ❌ NEVER exceed size limits without splitting

## Documentation Review Process (MANDATORY)

### Before Creating ANY Documentation

**You MUST perform these steps in order:**

1. **Review Existing Documentation Structure**
   ```
   - Check /docs folder and all subdirectories
   - List all existing documentation files
   - Identify relevant existing documents
   - Determine if content should be added to existing file or requires new file
   ```

2. **Check Document Size**
   ```
   - If updating existing document, check current line count
   - If document would exceed ~1000 lines, plan to split it
   - Identify logical split points (by topic, feature, or concern)
   ```

3. **Verify Proper Location**
   ```
   - Architecture docs → /docs/architecture/
   - Getting started → /docs/getting-started/
   - User guides → /docs/guides/
   - API documentation → /docs/api/
   - Troubleshooting → /docs/troubleshooting/
   - Implementation details (temporary) → code directory, marked for migration
   ```

4. **Avoid Duplication**
   ```
   - Search for similar content in existing docs
   - If found, update existing document instead
   - If content spans multiple docs, use cross-references
   - Never duplicate the same information in multiple files
   ```

5. **Plan Multi-Part Documents**
   ```
   If creating multi-part documentation:
   - Part 1: Overview, core concepts, getting started
   - Part 2: Advanced features, deep dives
   - Part 3: Integration, edge cases, troubleshooting
   - Each part: 500-1000 lines maximum
   - Include navigation links between parts at top and bottom
   ```

### Documentation Naming Conventions

**Single Documents:**
- Use kebab-case: `security-architecture.md`
- Be descriptive but concise: `api-authentication.md`
- Group by category in subdirectories

**Multi-Part Documents:**
- Format: `{topic}-part-{number}.md`
- Examples: 
  - `security-part-1.md` (Authentication)
  - `security-part-2.md` (Authorization)
  - `security-part-3.md` (Data Filtering)
- Include topic summary in first part
- Link to other parts prominently

### Documentation Location Rules

**MUST be in /docs:**
- Architecture decisions and designs
- User guides and tutorials
- API documentation
- Security guidelines
- Deployment instructions
- Troubleshooting guides
- ADRs (Architecture Decision Records)

**Can be in code directories:**
- README.md for specific modules
- Implementation notes (temporary, should migrate to /docs)
- Code-specific setup instructions
- Migration guides (during active development)

**Must be moved from code to /docs:**
- When implementation stabilizes
- Before production release
- When referenced by multiple components
- When needed by users/operators

## Critical Thinking Guidelines

- Begin with the desired outcome and audience; clarify what decision the documentation must enable.
- Map constraints first: required instruction files, linked docs, domain rules, and security/privacy expectations.
- Compare instructions for conflicts; prefer higher-priority/project-specific rules and call out trade-offs when they exist.
- Identify risks and edge cases (breaking changes, migrations, data loss, auth impacts); note any open questions instead of guessing.
- Support statements with evidence: cite file paths, sections, and line numbers when referencing sources or decisions.
- Validate completeness before finalizing: requirements coverage, affected docs, examples, diagrams, and testing instructions.
- Perform a final sanity check: is guidance actionable, minimal, and consistent; are next steps and owners obvious.
- What if the current solution is wrong? Pause to re-evaluate assumptions, list alternative approaches, outline rollback/migration steps, and flag blocking questions instead of forcing a shaky answer.

### When to Apply

- Run this checklist **before drafting, editing, or approving any documentation or source code files** so issues surface early.
- Re-apply after significant feedback or scope changes to confirm the guidance still holds.
- **ALWAYS run the Documentation Review Process before creating or modifying ANY documentation**

## Documentation Structure Requirements

### Repository Root Files (Mandatory)

Every repository MUST contain these files at the root level:

1. **README.md** - Project overview and quick start
2. **CHANGELOG.md** - Version history and changes
3. **LICENSE** - Legal terms
4. **CONTRIBUTING.md** - Contribution guidelines (for open source projects)
5. **.github/PULL_REQUEST_TEMPLATE.md** - PR template
6. **.github/ISSUE_TEMPLATE/** - Issue templates

### Centralized Documentation Directory (/docs)

**CRITICAL RULE**: ALL project documentation MUST be organized in the `/docs` folder.

#### Standard Directory Structure

```
docs/
├── getting-started/          # Quick start guides, installation
│   ├── installation.md
│   ├── quick-start.md
│   └── configuration.md
├── architecture/             # Architecture designs and decisions
│   ├── overview.md
│   ├── database-schema.md
│   ├── api-design.md
│   ├── security.md
│   └── security-part-1.md   # Example of split document
├── guides/                   # User and developer guides
│   ├── user-guide.md
│   ├── developer-guide.md
│   └── deployment-guide.md
├── api/                      # API documentation
│   ├── authentication.md
│   ├── endpoints.md
│   └── webhooks.md
├── troubleshooting/          # Common issues and solutions
│   ├── common-issues.md
│   ├── debugging.md
│   └── faq.md
├── decisions/                # Architecture Decision Records
│   ├── adr-001-architecture-choice.md
│   └── adr-002-database-selection.md
└── reference/                # Reference materials
    ├── glossary.md
    ├── configuration-reference.md
    └── environment-variables.md
```

#### Directory Purpose Guidelines

| Directory | Purpose | Max File Size | When to Split |
|-----------|---------|---------------|---------------|
| `getting-started/` | Quick onboarding, first-time setup | 500 lines | By component or platform |
| `architecture/` | Technical design, patterns, decisions | 1000 lines | By subsystem or concern |
| `guides/` | Step-by-step tutorials, how-tos | 800 lines | By user persona or feature |
| `api/` | Endpoint specs, contracts | 1000 lines | By resource or version |
| `troubleshooting/` | Problem solving, FAQs | 600 lines | By category or severity |
| `decisions/` | ADRs (one per file) | 500 lines | Always separate files |
| `reference/` | Lookup tables, configs | 1000 lines | By topic or resource |

#### Documentation Organization Rules

1. **Before creating a new document:**
   - Use file_search or list_dir to explore /docs
   - Check if similar content exists
   - Determine proper subdirectory

2. **When updating existing document:**
   - Check current line count
   - If approaching 1000 lines, plan to split
   - Maintain backward compatibility (keep old links working)

3. **When splitting a document:**
   - Create clear part divisions (e.g., `topic-part-1.md`, `topic-part-2.md`)
   - Add navigation header to each part
   - Update all references to point to correct part
   - Keep original filename as redirect or index

4. **Cross-referencing:**
   - Use relative paths: `[Security](../architecture/security.md)`
   - Link to specific sections: `[Auth Flow](../architecture/security.md#authentication-flow)`
   - Keep a central index in main README if structure is complex

## README.md Requirements

### Mandatory Sections

Every README.md MUST contain these sections in this order:

1. **Project Name and Description**
   - One-line description (max 160 characters)
   - Badges (build status, version, license, coverage)

2. **Table of Contents** (for files > 200 lines)

3. **Features**
   - Current features (✅)
   - In-progress features (🚧)
   - Planned features (📋)

4. **Prerequisites**
   - Exact version requirements
   - Platform requirements
   - Required tools

5. **Installation**
   - Step-by-step instructions
   - Separate sections for different components
   - Docker option if available

6. **Quick Start**
   - Minimal steps to run the application
   - Expected output
   - Next steps

7. **Configuration**
   - Environment variables table
   - Configuration file explanations
   - Security considerations

8. **Project Structure**
   - Directory tree
   - Explanation of major directories
   - Links to detailed architecture docs

9. **Development**
   - Development environment setup
   - Build instructions
   - Testing instructions
   - Debugging tips

10. **Contributing**
    - Link to CONTRIBUTING.md
    - Code of conduct reference

11. **License**
    - License type
    - Link to LICENSE file

12. **Support**
    - How to get help
    - Issue tracker link
    - Contact information

### README.md Update Rules

**MUST update README.md when:**
- Adding/removing major features
- Changing installation steps
- Updating prerequisites
- Modifying configuration options
- Changing project structure
- Updating support channels

**Update within same commit** as the code change.

## CHANGELOG.md Requirements

### Format Rules

MUST follow [Keep a Changelog](https://keepachangelog.com/) format:

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
### Changed
### Deprecated
### Removed
### Fixed
### Security

## [Version] - YYYY-MM-DD
```

### Category Definitions

- **Added**: New features, endpoints, capabilities
- **Changed**: Changes to existing functionality
- **Deprecated**: Features marked for removal (include removal timeline)
- **Removed**: Removed features (must include migration guide)
- **Fixed**: Bug fixes (include issue reference)
- **Security**: Security fixes (CVE references if applicable)

### Changelog Update Rules

**MUST add to CHANGELOG.md when:**
- Merging any PR that affects users
- Fixing any bug
- Adding any feature
- Changing any API
- Updating dependencies with breaking changes
- Deploying to production

**Entry Requirements:**
- Write from user perspective (not implementation details)
- Include issue/PR reference: `(#123)`
- Be specific: "Fixed null reference in customer search" not "Fixed bug"
- For breaking changes, add `BREAKING CHANGE:` prefix
- For security fixes, add `SECURITY:` prefix

**Version Sections:**
- Add to `[Unreleased]` during development
- Move to versioned section on release
- Include release date
- Include comparison links at bottom

## API Documentation Requirements

### When API Documentation is Required

Create/update API documentation when:
- Adding new endpoints
- Modifying endpoint behavior
- Changing request/response formats
- Adding authentication requirements
- Changing error responses
- Updating rate limits

### Required API Documentation Elements

For each endpoint, document:

1. **HTTP Method and Path**
   ```
   POST /api/customers
   ```

2. **Description**
   - What the endpoint does
   - When to use it

3. **Authentication**
   - Required auth type
   - Required permissions/roles

4. **Request**
   - Headers (with examples)
   - Path parameters
   - Query parameters (with defaults, constraints)
   - Request body (with schema and example)

5. **Response**
   - Success response (status code, schema, example)
   - Error responses (all possible status codes)
   - Headers

6. **Examples**
   - Complete curl examples
   - Request/response pairs
   - Error scenarios

7. **Notes**
   - Rate limits
   - Pagination details
   - Sorting options
   - Filtering capabilities

### API Documentation Update Process

1. **Before implementing endpoint**: Write API documentation as specification
2. **During implementation**: Update documentation to match reality
3. **After implementation**: Verify documentation accuracy
4. **In same PR**: Include API documentation updates

## Architecture Documentation Requirements

### When to Create/Update Architecture Docs

**MUST update architecture documentation when:**
- Adding new layers or components
- Changing communication patterns
- Modifying database schema
- Updating technology stack
- Changing deployment architecture
- Implementing new design patterns

### Required Architecture Documents

1. **architecture/overview.md**
   - System architecture diagram
   - Layer descriptions
   - Component interactions
   - Technology stack with versions

2. **architecture/database-schema.md**
   - Entity relationship diagrams
   - Table descriptions
   - Index strategies
   - Migration approach

3. **architecture/api-design.md**
   - API design principles
   - Versioning strategy
   - Authentication/authorization flow
   - Error handling approach

4. **architecture/security.md**
   - Security architecture
   - Authentication mechanisms
   - Authorization model
   - Data protection measures
   - Security best practices

### Architecture Documentation Rules

- Use diagrams for complex concepts
- Explain WHY decisions were made, not just WHAT
- Link to Architecture Decision Records (ADRs)
- Keep diagrams in source control (use Mermaid, PlantUML)
- Update diagrams when code structure changes

## Architecture Decision Records (ADR)

### When to Create ADR

Create an ADR when making decisions about:
- Technology choices
- Architectural patterns
- Database selection
- API design approaches
- Security implementations
- Deployment strategies

### ADR Format (Mandatory)

```markdown
# ADR-NNN: [Decision Title]

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-XXX]

## Context
[What is the issue we're seeing that is motivating this decision?]
[What are the constraints?]
[What are the business requirements?]

## Decision
[What is the change we're proposing and/or doing?]

## Consequences
### Positive
- [What becomes easier?]
- [What becomes possible?]

### Negative
- [What becomes harder?]
- [What trade-offs are we accepting?]

### Neutral
- [What remains unchanged?]

## Alternatives Considered
### Alternative 1
- Description
- Pros
- Cons
- Why rejected

## References
- [Links to relevant resources]
- [Related ADRs]
- [Documentation]

## Metadata
- Date: YYYY-MM-DD
- Author: [Name]
- Reviewers: [Names]
```

### ADR Naming Convention

```
docs/decisions/adr-001-use-postgresql-database.md
docs/decisions/adr-002-implement-cqrs-pattern.md
docs/decisions/adr-003-choose-vuejs-frontend.md
```

### ADR Update Rules

- **Never delete ADRs** - Mark as deprecated or superseded
- **Never edit decisions** - Create new ADR that supersedes
- Update status when decisions change
- Link related ADRs bidirectionally

## Component/Module Documentation

### When Component Docs are Required

Create component-level README when component:
- Has complex responsibility
- Used by multiple other components
- Has non-obvious configuration
- Provides reusable functionality
- Requires specific setup

### Component README Requirements

Each component README.md MUST contain:

1. **Purpose**: What this component does
2. **Responsibilities**: Specific duties
3. **Dependencies**: What it depends on and why
4. **Public API**: Interfaces, classes, methods
5. **Configuration**: Component-specific settings
6. **Usage Examples**: Code samples
7. **Testing**: How to test this component

### Component Documentation Update Rules

Update component README when:
- Public API changes
- Dependencies change
- Configuration options change
- Usage patterns change

## Code Comments and Documentation

### When Comments are Required

**MUST add comments for:**
- Complex algorithms (explain the approach)
- Non-obvious business rules (explain the why)
- Workarounds (explain the problem and why this fixes it)
- Public APIs (XML documentation in C#)
- Performance optimizations (explain the trade-off)
- Security-sensitive code (explain the protection)

### Comment Quality Rules

**Good Comments:**
```csharp
// Retry logic handles transient database connection failures
// that occur during peak load. Max 3 retries with exponential backoff.
```

**Bad Comments:**
```csharp
// Loop through customers
// Check if email is null
```

**Rules:**
- Explain WHY, not WHAT (code shows what)
- Update comments when code changes
- Remove obsolete comments immediately
- Don't comment out code (use version control)

### XML Documentation (C#)

**Required for all public APIs:**

```csharp
/// <summary>
/// Retrieves a customer by their unique identifier.
/// </summary>
/// <param name="id">The unique identifier of the customer.</param>
/// <returns>The customer if found; otherwise, null.</returns>
/// <exception cref="ArgumentException">Thrown when id is empty.</exception>
public async Task<Customer?> GetByIdAsync(Guid id)
```

## Troubleshooting Documentation

### When to Add Troubleshooting Entry

Add troubleshooting entry when:
- Bug is reported more than once
- Error message is cryptic
- Solution is non-obvious
- Common setup mistake identified
- Environment-specific issue found

### Troubleshooting Entry Format

```markdown
### [Descriptive Problem Title]

**Problem:**
```
[Exact error message or symptom]
```

**Symptoms:**
- Observable behavior 1
- Observable behavior 2

**Root Cause:**
[What actually causes this]

**Solution:**
```bash
# Step-by-step commands
```

**Prevention:**
[How to avoid this issue]

**Related Issues:** #123, #456
```

### Troubleshooting Organization

Group by:
1. Installation Issues
2. Configuration Issues
3. Database Issues
4. Authentication Issues
5. Build/Deployment Issues
6. Runtime Issues
7. Performance Issues

## Inline Documentation Maintenance

### Documentation Synchronization Rules

**CRITICAL: Always update documentation in the same commit as code changes.**

When you make code changes, check and update:
- [ ] Inline code comments
- [ ] XML documentation
- [ ] README.md (if public behavior changes)
- [ ] API documentation (if endpoints change)
- [ ] Architecture docs (if structure changes)
- [ ] CHANGELOG.md (always)
- [ ] Migration guides (if breaking changes)

### Documentation Review Checklist

Before committing, verify:
- [ ] All new public APIs have XML documentation
- [ ] README reflects current installation steps
- [ ] Configuration examples are accurate
- [ ] Code examples compile and run
- [ ] Links are not broken
- [ ] Version numbers are current
- [ ] Screenshots show current UI (if applicable)
- [ ] CHANGELOG entry added

## Version-Specific Documentation

### Versioning Rules

- Maintain documentation for all supported versions
- Clearly mark version-specific features
- Provide migration guides for breaking changes
- Archive documentation for unsupported versions

### Version Indicators

Use clear version indicators:

```markdown
## Feature X

> **Available since:** v1.2.0  
> **Deprecated in:** v2.0.0  
> **Removed in:** v3.0.0

[Feature documentation]

### Migration to Alternative

[Migration guide to replacement feature]
```

### Breaking Change Documentation

For breaking changes, MUST provide:

1. **What changed**: Specific API/behavior change
2. **Why it changed**: Reason for the change
3. **Migration path**: Step-by-step migration
4. **Code examples**: Before and after
5. **Timeline**: When deprecation starts, when removal happens

## Documentation Quality Standards

### Writing Style Requirements

**Mandatory Rules:**
- Use present tense ("Returns a list" not "Will return")
- Use active voice ("Create a customer" not "A customer is created")
- Be concise (remove filler words)
- Be specific (use exact names, versions, values)
- Use consistent terminology
- Define acronyms on first use

### Formatting Requirements

**Code Blocks:**
- Always specify language for syntax highlighting
- Include complete, runnable examples
- Show expected output
- Indicate which commands need modification

```markdown
# Good
```bash
# Replace YOUR_API_KEY with your actual key
export API_KEY=YOUR_API_KEY
```

# Bad
```
Set your API key
```
```

**Lists:**
- Use numbered lists for sequential steps
- Use bullet lists for unordered items
- Keep list items parallel in structure
- Don't mix ordered and unordered at same level

**Links:**
- Use descriptive link text (not "click here")
- Verify links are not broken
- Use relative links for internal docs
- Use absolute links for external resources

### Example Quality Requirements

Every code example MUST:
- Be complete and runnable
- Include necessary imports/usings
- Show realistic scenarios
- Include error handling
- Be tested and verified working
- Match current codebase style

**Good Example:**
```csharp
using SmallCRM.Application.Services;
using SmallCRM.Domain.Entities;

// Create customer service with dependency injection
var customerService = serviceProvider.GetRequiredService<ICustomerService>();

// Create new customer
var customer = new Customer 
{
    FirstName = "John",
    LastName = "Doe",
    Email = "john.doe@example.com"
};

try 
{
    var result = await customerService.CreateAsync(customer);
    Console.WriteLine($"Created customer with ID: {result.Id}");
}
catch (ValidationException ex)
{
    Console.WriteLine($"Validation failed: {ex.Message}");
}
```

**Bad Example:**
```csharp
// Create a customer
var customer = new Customer();
service.Create(customer);
```

## Documentation Automation

### Auto-Generated Documentation

**When to use auto-generation:**
- API documentation from OpenAPI/Swagger
- Class documentation from XML comments
- Database schema from migrations
- Configuration reference from code

**Rules for auto-generated docs:**
- Run generation on every build
- Commit generated docs to repository
- Review generated output for accuracy
- Supplement with hand-written guides

### Documentation Testing

**Implement these checks:**
- Link checker in CI/CD
- Spell checker
- Code example compilation tests
- Markdown linting
- Version consistency checks

### CI/CD Integration

In CI/CD pipeline, MUST:
- Build documentation
- Run link checker
- Verify code examples compile
- Check for broken references
- Deploy to documentation site (if applicable)

## Documentation Maintenance Schedule

### Continuous Maintenance

**With every PR:**
- Update affected documentation
- Update CHANGELOG.md
- Add/update code examples
- Verify links still work

### Regular Reviews

**Weekly:**
- Review open documentation issues
- Update troubleshooting with new issues
- Check for outdated screenshots

**Monthly:**
- Review README for accuracy
- Check all links
- Update version references
- Review and update examples

**Quarterly:**
- Full documentation audit
- Archive old version docs
- Update architecture diagrams
- Review and consolidate FAQs

**Before Each Release:**
- Move CHANGELOG unreleased to version section
- Update version numbers everywhere
- Verify migration guides
- Update installation instructions
- Review API documentation
- Check deprecated features documentation

## Documentation Anti-Patterns (Avoid These)

### Don't Do This

❌ **Outdated information**
- Consequence: Users follow wrong steps, waste time
- Prevention: Update docs in same commit as code

❌ **Missing prerequisites**
- Consequence: Users can't complete setup
- Prevention: Test installation steps from scratch

❌ **Incomplete examples**
- Consequence: Users can't run the code
- Prevention: Test all examples before committing

❌ **Broken links**
- Consequence: Users can't find referenced information
- Prevention: Run link checker in CI

❌ **Wall of text**
- Consequence: Users can't find information
- Prevention: Use headings, lists, tables, code blocks

❌ **Assuming knowledge**
- Consequence: Beginners are lost
- Prevention: Define terms, explain prerequisites

❌ **No migration guides for breaking changes**
- Consequence: Users can't upgrade
- Prevention: Write migration guide before implementing change

❌ **Duplicated information**
- Consequence: Information becomes inconsistent
- Prevention: Single source of truth, use links

❌ **Comments that just repeat code**
- Consequence: Noise, maintenance burden
- Prevention: Explain why, not what

❌ **Committing commented-out code**
- Consequence: Confusion about what's active
- Prevention: Use version control, delete old code

## Special Documentation Types

### Migration Guides

Create migration guide when:
- Major version update
- Breaking API changes
- Database schema changes
- Configuration format changes

**Migration Guide Structure:**
```markdown
# Migration Guide: v1.x to v2.0

## Overview
- Summary of major changes
- Timeline and support policy
- Estimated migration effort

## Breaking Changes

### Change 1: [Description]

**What changed:**
[Detailed explanation]

**Why it changed:**
[Rationale]

**Before (v1.x):**
```csharp
[Old code example]
```

**After (v2.0):**
```csharp
[New code example]
```

**Migration Steps:**
1. Step one
2. Step two

### Database Migrations
[SQL scripts or EF commands]

### Configuration Changes
[Old vs new configuration]

## Deprecation Timeline
- v1.5: Features deprecated, warnings added
- v2.0: Breaking changes implemented
- v2.1: Deprecated features removed

## Getting Help
[Support resources]
```

### Runbooks

Create runbooks for:
- Deployment procedures
- Incident response
- Backup and recovery
- Common operations tasks

**Runbook Structure:**
```markdown
# Runbook: [Task Name]

## Purpose
[What this accomplishes]

## When to Use
[Triggers or scenarios]

## Prerequisites
- Permission requirements
- Required access
- Required tools

## Procedure

### Step 1: [Action]
```bash
[Commands]
```

**Expected output:**
```
[Sample output]
```

**If this fails:**
[Troubleshooting steps]

### Step 2: [Action]
[Continue...]

## Verification
[How to verify success]

## Rollback
[How to undo if needed]

## Related Procedures
- Link to related runbook 1
- Link to related runbook 2
```

### Glossary

Maintain a glossary when:
- Project has domain-specific terminology
- Acronyms are used frequently
- Terms have special meaning in context

**Glossary Format:**
```markdown
# Glossary

## A

**API (Application Programming Interface)**
A set of protocols and tools for building software applications.

**Authentication**
Process of verifying identity of a user or system.

## B

**Backend**
Server-side application logic and database management.
```

## Documentation Templates

### Create Templates For

Maintain templates for:
- Pull Request descriptions
- Issue reports (bug, feature, question)
- ADR (Architecture Decision Record)
- Component README
- API endpoint documentation
- Troubleshooting entries

### Template Usage Rules

- Store in `.github/` directory
- Keep templates up to date
- Make templates easy to fill out
- Include examples in comments
- Mark required vs optional sections

## Documentation Metrics

### Track These Metrics

**Coverage:**
- % of public APIs with documentation
- % of features documented
- % of known issues in troubleshooting

**Quality:**
- Number of broken links
- Number of outdated screenshots
- Documentation age (days since last update)

**Effectiveness:**
- Issues resolved by documentation
- Time to complete setup following docs
- User feedback on documentation

### Documentation Debt

**Identify documentation debt:**
- Missing documentation for features
- Outdated information
- Incomplete examples
- Broken links
- Missing migration guides

**Address documentation debt:**
- Track in issue tracker
- Label as "documentation"
- Prioritize with feature work
- Assign during sprint planning

## Emergency Documentation Updates

### Hotfix Documentation

When deploying hotfix:
1. Update CHANGELOG.md with security/critical flag
2. Update affected documentation
3. Notify users via appropriate channels
4. Add to troubleshooting if relevant
5. Create post-mortem document

### Post-Mortem Documentation

After incidents, create:
```markdown
# Post-Mortem: [Incident Name]

## Date
[When incident occurred]

## Impact
- [What was affected]
- [How many users impacted]
- [Duration of impact]

## Timeline
- **HH:MM** - [Event]
- **HH:MM** - [Event]

## Root Cause
[What caused the incident]

## Resolution
[How it was fixed]

## Action Items
- [ ] Update documentation (link)
- [ ] Add monitoring (ticket)
- [ ] Improve error handling (ticket)

## Lessons Learned
[What we learned]

## Related Documentation
- [Link to troubleshooting entry added]
- [Link to runbook created]
```

## Summary: Your Documentation Responsibilities

As an AI assistant, when working on this project:

### For Every Code Change

✅ **MUST:**
- Update CHANGELOG.md
- Update affected documentation in same commit
- Update code comments
- Verify examples still work
- Check README accuracy
- Update API docs if endpoints changed

### For New Features

✅ **MUST:**
- Write documentation before implementing
- Add usage examples
- Add to README features list
- Document configuration options
- Add troubleshooting section if needed
- Update architecture docs if structure changed

### For Bug Fixes

✅ **MUST:**
- Add to CHANGELOG
- Add to troubleshooting if recurring
- Update incorrect documentation
- Fix outdated examples

### For Breaking Changes

✅ **MUST:**
- Write migration guide
- Update all affected documentation
- Mark old way as deprecated
- Provide before/after examples
- Update CHANGELOG with BREAKING CHANGE prefix
- Document deprecation timeline

### Documentation Quality Checks

✅ **Before every commit:**
- [ ] Documentation is accurate
- [ ] Examples are complete and tested
- [ ] Links are not broken
- [ ] Terminology is consistent
- [ ] CHANGELOG is updated
- [ ] Version numbers are correct
- [ ] No outdated information
- [ ] No duplicated content
- [ ] Reviewed /docs folder structure
- [ ] Verified no duplication with existing docs
- [ ] Checked document size (<1000 lines)
- [ ] Split oversized documents appropriately

## Practical Examples: Documentation Review Process

### Example 1: Creating Security Documentation

**Scenario**: Need to document the security implementation for the backend.

**WRONG Approach** ❌:
```
1. Create SECURITY_IMPLEMENTATION.md in src/small-crm-backend/
2. Create MIGRATION_GUIDE.md in src/small-crm-backend/
3. Create CHECKLIST.md in src/small-crm-backend/
4. Write 400+ lines each
```

**CORRECT Approach** ✅:
```
1. Review /docs folder structure:
   - Found: /docs/architecture/security.md exists (1000+ lines)
   
2. Determine if update or new file needed:
   - Existing security.md covers architecture/design
   - Need implementation details and setup guide
   - Requires new document
   
3. Check proper location:
   - Implementation guide → /docs/guides/security-implementation.md
   - Migration steps → /docs/guides/database-migration.md
   - OR combine if related: /docs/guides/security-setup.md
   
4. Check size:
   - If combined guide would be 800-1000 lines: OK
   - If over 1000 lines: Split into parts
     - /docs/guides/security-setup-part-1.md (Architecture & Setup)
     - /docs/guides/security-setup-part-2.md (Implementation & Testing)
   
5. Update cross-references:
   - Link from /docs/architecture/security.md to implementation guide
   - Link from main README.md to setup guide
   - Add to /docs/README.md if it exists
```

### Example 2: Updating Existing Documentation

**Scenario**: Adding new authentication endpoints to existing API.

**Review Process**:
```
1. Check /docs/api/ folder:
   - Found: /docs/api/endpoints.md (current: 850 lines)
   - Found: /docs/api/authentication.md (current: 300 lines)
   
2. Decide where to add:
   - New auth endpoints → /docs/api/authentication.md (better fit)
   - Would bring total to ~500 lines (well under limit)
   
3. Update document:
   - Add new endpoints to authentication.md
   - Update any examples in endpoints.md that reference auth
   - Add cross-reference link if needed
   
4. Update related docs:
   - Check /docs/architecture/security.md for impact
   - Update /docs/getting-started/quick-start.md if auth flow changed
   - Update CHANGELOG.md
```

### Example 3: Document Size Exceeded

**Scenario**: API documentation reached 1200 lines.

**Splitting Strategy**:
```
Original: /docs/api/endpoints.md (1200 lines)

Split into:
- /docs/api/endpoints-part-1.md (800 lines)
  - Authentication endpoints
  - User management endpoints
  - Core CRUD operations
  
- /docs/api/endpoints-part-2.md (400 lines)
  - Advanced features
  - Bulk operations
  - Webhooks
  - Integration endpoints

Add navigation:
---
📚 **API Endpoints Documentation**
- [Part 1: Core APIs](./endpoints-part-1.md) ← You are here
- [Part 2: Advanced APIs](./endpoints-part-2.md)
---

Update references:
- Change links in other docs to point to correct part
- OR create /docs/api/endpoints.md as index/redirect page
```

### Example 4: Avoiding Duplication

**Scenario**: Need to document JWT configuration.

**Review Process**:
```
1. Search existing docs:
   grep -r "JWT" /docs/
   - Found in: /docs/architecture/security.md
   - Found in: /docs/guides/configuration.md
   
2. Analyze existing content:
   - security.md: JWT architecture, token structure
   - configuration.md: Basic JWT environment variables
   
3. Determine approach:
   ❌ Don't create new JWT.md (would duplicate)
   ✅ Enhance existing sections:
      - Update /docs/guides/configuration.md with detailed JWT settings
      - Link from security.md to configuration.md for setup details
      - Keep architecture separate from configuration
```

### Example 5: Migration from Code to /docs

**Scenario**: SECURITY_IMPLEMENTATION.md in src/small-crm-backend/ needs proper home.

**Migration Steps**:
```
1. Review content:
   - 400 lines of implementation guide
   - Combines architecture, setup, and testing
   
2. Determine proper location:
   - Architecture content → /docs/architecture/security.md (update)
   - Setup content → /docs/guides/security-setup.md (new)
   - Testing content → /docs/guides/security-testing.md (new)
   OR if combined < 1000 lines:
   - All in → /docs/guides/security-implementation.md
   
3. Migrate:
   - Create/update target files in /docs
   - Update all references
   - Add deprecation notice to old location
   - After stabilization, remove from code directory
   
4. Update navigation:
   - Add to main README.md
   - Update /docs/architecture/security.md with link
   - Update CHANGELOG.md
```

## Documentation is Not Optional

Remember: **Undocumented features don't exist.** Code without documentation is incomplete. Treat documentation updates with the same importance as code changes. Users judge software quality by documentation quality.

**Key Reminders:**
- Always review /docs folder before creating new documentation
- Keep documents under 1000 lines for readability
- Split large documents into logical parts
- Update existing documents instead of creating duplicates
- Store all project documentation in /docs folder
- Temporary implementation notes can stay in code directories but should migrate to /docs

**Good documentation = Maintainable software = Happy developers = Successful project**
