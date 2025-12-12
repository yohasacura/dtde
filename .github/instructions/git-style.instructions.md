# Git Style Guide

## Overview
This guide establishes standards for Git usage to maintain a clean, comprehensible, and stable version control history. Following these practices ensures that the repository remains navigable, collaborative changes are clear, and the project history tells a coherent story.

## Commit Messages

### Structure
Every commit message should follow this format:

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Type
Must be one of the following:

- **feat**: A new feature for the user
- **fix**: A bug fix
- **docs**: Documentation only changes
- **style**: Changes that do not affect the meaning of the code (white-space, formatting, missing semi-colons, etc)
- **refactor**: A code change that neither fixes a bug nor adds a feature
- **perf**: A code change that improves performance
- **test**: Adding missing tests or correcting existing tests
- **build**: Changes that affect the build system or external dependencies
- **ci**: Changes to CI configuration files and scripts
- **chore**: Other changes that don't modify src or test files
- **revert**: Reverts a previous commit

### Scope
The scope should specify the place of the commit change. For this project:

- **backend**: Changes to the backend API
- **frontend**: Changes to the frontend application
- **api**: REST API endpoints or services
- **ui**: User interface components
- **auth**: Authentication/authorization
- **db**: Database schema or migrations
- **config**: Configuration files
- **deps**: Dependencies updates

### Subject
The subject contains a succinct description of the change:

- Use the imperative, present tense: "change" not "changed" nor "changes"
- Don't capitalize the first letter
- No period (.) at the end
- Maximum 50 characters
- Be specific and descriptive

### Body (Optional but Recommended)
The body should include:

- Motivation for the change
- Contrast with previous behavior
- Wrap at 72 characters
- Use bullet points for multiple items

### Footer (Optional)
The footer should contain:

- **Breaking Changes**: Start with `BREAKING CHANGE:` followed by description
- **Issue References**: `Closes #123`, `Fixes #456`, `Relates to #789`

### Examples

#### Good Commit Messages

```
feat(auth): add JWT token refresh mechanism

Implement automatic token refresh to improve user experience by
preventing unexpected logouts. The refresh token is now stored
securely and used to obtain new access tokens before expiration.

- Add refresh token endpoint
- Implement token refresh interceptor
- Update authentication service

Closes #42
```

```
fix(api): handle null values in customer search

Previous implementation threw NullReferenceException when customers
had null email addresses. Added proper null checking and filtering.

Fixes #156
```

```
docs(readme): update installation instructions

Add missing steps for database setup and clarify Node.js version
requirements.
```

```
refactor(backend): extract customer validation logic

Move validation logic from controller to separate validator class
following single responsibility principle. No functional changes.
```

#### Bad Commit Messages

```
❌ Update stuff
❌ Fixed bug
❌ WIP
❌ asdfgh
❌ Updated files and things
❌ Final commit (really this time)
```

## Branch Naming

### Convention
Use lowercase with hyphens to separate words:

```
<type>/<issue-number>-<brief-description>
```

### Types
- **feature/** - New features
- **bugfix/** - Bug fixes
- **hotfix/** - Urgent production fixes
- **refactor/** - Code refactoring
- **docs/** - Documentation updates
- **test/** - Test additions or modifications
- **chore/** - Maintenance tasks

### Examples
```
feature/123-customer-import
bugfix/456-fix-login-validation
hotfix/789-critical-security-patch
refactor/101-clean-architecture-implementation
docs/update-api-documentation
test/add-integration-tests
```

## Branching Strategy

### Main Branches
- **main**: Production-ready code, always stable
- **develop**: Integration branch for features, should be stable

### Supporting Branches
- **feature/**: Branch from develop, merge back to develop
- **bugfix/**: Branch from develop, merge back to develop
- **hotfix/**: Branch from main, merge to both main and develop
- **release/**: Branch from develop, merge to both main and develop

### Workflow

1. **Starting new work**
   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b feature/123-new-feature
   ```

2. **Regular commits**
   ```bash
   git add .
   git commit -m "feat(scope): descriptive message"
   ```

3. **Keep branch updated**
   ```bash
   git checkout develop
   git pull origin develop
   git checkout feature/123-new-feature
   git rebase develop
   ```

4. **Finishing work**
   ```bash
   git push origin feature/123-new-feature
   # Create Pull Request
   ```

## Pull Requests

### Title
Follow the same convention as commit messages:
```
<type>(<scope>): <description>
```

### Description Template
```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
Describe testing performed

## Checklist
- [ ] Code follows project style guidelines
- [ ] Self-review completed
- [ ] Comments added for complex code
- [ ] Documentation updated
- [ ] No new warnings generated
- [ ] Tests added/updated
- [ ] All tests passing
- [ ] No merge conflicts

## Related Issues
Closes #issue_number
```

### Review Process
- Require at least one approval before merging
- Address all review comments or provide justification
- Ensure CI/CD pipeline passes
- Squash commits when appropriate to maintain clean history

## Best Practices

### Commits

1. **Atomic Commits**: Each commit should represent a single logical change
2. **Commit Often**: Small, frequent commits are better than large, infrequent ones
3. **Don't Commit Half-Done Work**: Commit when a logical unit is complete
4. **Test Before Commit**: Ensure code compiles and tests pass
5. **Don't Commit Generated Files**: Use .gitignore appropriately

### History Management

1. **Use Rebase for Feature Branches**: Keep a linear history
   ```bash
   git rebase develop
   ```

2. **Use Merge for Main Integration**: Preserve branch history
   ```bash
   git merge --no-ff feature/123-new-feature
   ```

3. **Never Rewrite Public History**: Don't force push to shared branches
   ```bash
   # ❌ Never do this on main or develop
   git push --force origin main
   ```

4. **Squash When Appropriate**: Combine related commits before merging
   ```bash
   git rebase -i HEAD~3
   ```

### Collaboration

1. **Pull Before You Push**: Always sync before pushing
   ```bash
   git pull --rebase origin develop
   ```

2. **Communicate Breaking Changes**: Use BREAKING CHANGE in commit footer
3. **Keep PRs Focused**: One feature or fix per PR
4. **Update Branch Description**: Keep branch purpose clear

## Git Commands Reference

### Daily Workflow
```bash
# Start new feature
git checkout develop
git pull origin develop
git checkout -b feature/123-description

# Make changes and commit
git add <files>
git commit -m "feat(scope): description"

# Push changes
git push origin feature/123-description

# Update with latest develop
git fetch origin
git rebase origin/develop

# Interactive rebase to clean up commits
git rebase -i origin/develop
```

### Fixing Mistakes
```bash
# Amend last commit (before pushing)
git commit --amend

# Undo last commit (keep changes)
git reset --soft HEAD~1

# Undo last commit (discard changes)
git reset --hard HEAD~1

# Revert a commit (create new commit)
git revert <commit-hash>
```

### Viewing History
```bash
# View commit history
git log --oneline --graph --decorate --all

# View changes in a commit
git show <commit-hash>

# View file history
git log --follow -p -- <file>
```

## Versioning

Follow Semantic Versioning (SemVer): `MAJOR.MINOR.PATCH`

- **MAJOR**: Incompatible API changes
- **MINOR**: Backward-compatible functionality
- **PATCH**: Backward-compatible bug fixes

### Tagging Releases
```bash
# Create annotated tag
git tag -a v1.2.3 -m "Release version 1.2.3"

# Push tag
git push origin v1.2.3

# List tags
git tag -l
```

## .gitignore Strategy

### Categories to Ignore

1. **Build Artifacts**: bin/, obj/, dist/, build/
2. **Dependencies**: node_modules/, packages/
3. **IDE Files**: .vs/, .vscode/, .idea/
4. **OS Files**: .DS_Store, Thumbs.db
5. **Secrets**: *.env, appsettings.Development.json, secrets.json
6. **Logs**: *.log, logs/
7. **Temporary Files**: *.tmp, *.temp, *.swp

### Never Commit
- Passwords or API keys
- Personal configuration
- Large binary files (use Git LFS)
- Files that can be regenerated

## Merge Conflicts

### Resolution Steps

1. **Update your branch**
   ```bash
   git fetch origin
   git rebase origin/develop
   ```

2. **Resolve conflicts**
   - Open conflicted files
   - Look for conflict markers: `<<<<<<<`, `=======`, `>>>>>>>`
   - Edit to resolve
   - Test the resolution

3. **Complete the rebase**
   ```bash
   git add <resolved-files>
   git rebase --continue
   ```

4. **Push changes**
   ```bash
   git push origin feature/123-description --force-with-lease
   ```

## Code Review Guidelines

### As Author
- Keep PRs small and focused
- Write clear PR descriptions
- Respond promptly to feedback
- Update PR based on comments
- Don't take feedback personally

### As Reviewer
- Review promptly
- Be constructive and specific
- Approve when satisfied
- Request changes if needed
- Explain the "why" behind suggestions

## Emergency Procedures

### Hotfix Process
```bash
# Create hotfix branch from main
git checkout main
git pull origin main
git checkout -b hotfix/critical-issue

# Make fix and commit
git add .
git commit -m "hotfix(scope): fix critical issue"

# Merge to main
git checkout main
git merge --no-ff hotfix/critical-issue
git tag -a v1.2.4 -m "Hotfix release 1.2.4"
git push origin main --tags

# Merge to develop
git checkout develop
git merge --no-ff hotfix/critical-issue
git push origin develop

# Delete hotfix branch
git branch -d hotfix/critical-issue
```

### Recovering Lost Commits
```bash
# View reference log
git reflog

# Restore to specific state
git reset --hard <commit-hash>
```

## Automation

### Pre-commit Hooks
Consider using tools like Husky to enforce:
- Commit message format validation
- Code linting
- Unit tests
- Prevent commits to protected branches

### CI/CD Integration
- Run tests on every push
- Deploy to staging from develop
- Deploy to production from main
- Automated version bumping
- Changelog generation

## Summary

A good Git history is:
- **Linear**: Easy to follow and understand
- **Atomic**: Each commit is a complete, logical unit
- **Descriptive**: Clear messages explain what and why
- **Stable**: No broken commits in main branches
- **Collaborative**: Facilitates team communication

Following this guide ensures that:
- New team members can understand project evolution
- Bugs can be tracked to specific changes
- Features can be safely rolled back
- Collaboration is smooth and conflicts are minimal
- The codebase maintains professional standards
