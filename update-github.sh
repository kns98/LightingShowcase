#!/usr/bin/env bash
set -Eeuo pipefail

# Upload a downloaded LightingShowcase source folder to GitHub without requiring
# the folder to have been created by `git clone`.
#
# Usage:
#   ./update-github.sh
#   ./update-github.sh <repository-url> <branch> "<commit message>"
#
# Defaults:
#   repository: https://github.com/kns98/LightingShowcase.git
#   branch:     master

DEFAULT_REPOSITORY_URL="https://github.com/kns98/LightingShowcase.git"
DEFAULT_BRANCH="master"
DEFAULT_COMMIT_MESSAGE="Separate Windows and Linux projects and document Linux Vulkan setup"

REPOSITORY_URL="${1:-$DEFAULT_REPOSITORY_URL}"
BRANCH="${2:-$DEFAULT_BRANCH}"
COMMIT_MESSAGE="${3:-$DEFAULT_COMMIT_MESSAGE}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

cd "$SCRIPT_DIR"

log() {
    printf '\n==> %s\n' "$*"
}

fail() {
    printf '\nERROR: %s\n' "$*" >&2
    exit 1
}

command -v git >/dev/null 2>&1 || fail "Git is not installed. Install it with: sudo apt install git"

# Do not allow the script to run from a nested copy of the source tree.
[[ -f "README.md" ]] || fail "README.md was not found. Put this script in the project root and run it there."

log "Project directory: $SCRIPT_DIR"
log "GitHub repository: $REPOSITORY_URL"
log "Target branch: $BRANCH"

# A ZIP extraction does not remove files that existed in an older copy of the
# folder. Delete the retired mixed-platform project files explicitly so GitHub
# cannot continue building the obsolete LightingShowcase.sln/project graph.
LEGACY_PROJECT_FILES=(
    "LightingShowcase.sln"
    "LightingShowcase.csproj"
    "LightingShowcase.CommandLine/LightingShowcase.CommandLine.csproj"
    "validate-platform-split.sh"
    "PORT_VALIDATION.txt"
)

for legacy_file in "${LEGACY_PROJECT_FILES[@]}"; do
    if [[ -e "$legacy_file" ]]; then
        log "Removing obsolete file: $legacy_file"
        rm -f -- "$legacy_file"
    fi
done

# Remove obsolete standalone workflow files left behind by an older extraction.
# Keep the primary build workflow supplied by this package.
if [[ -d .github/workflows ]]; then
    while IFS= read -r -d '' workflow_file; do
        if [[ "$workflow_file" != ".github/workflows/dotnet-desktop.yml" ]] \
            && grep -Fq 'validate-platform-split.sh' "$workflow_file"; then
            log "Removing obsolete validation workflow: $workflow_file"
            rm -f -- "$workflow_file"
        fi
    done < <(find .github/workflows -maxdepth 1 -type f \
        \( -name '*.yml' -o -name '*.yaml' \) -print0)
fi

# Refuse to upload an incomplete platform split. This also catches cases where
# hidden folders such as .github were not copied from the downloaded archive.
REQUIRED_FILES=(
    "LightingShowcase.Windows.sln"
    "LightingShowcase.Windows.csproj"
    "LightingShowcase.Linux.sln"
    "LightingShowcase.CommandLine/LightingShowcase.CommandLine.Windows.csproj"
    "LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj"
    ".github/workflows/dotnet-desktop.yml"
)

for required_file in "${REQUIRED_FILES[@]}"; do
    [[ -f "$required_file" ]] || fail "Required platform-split file is missing: $required_file"
done

if ! grep -Fq 'LightingShowcase.Windows.sln' .github/workflows/dotnet-desktop.yml; then
    fail "The GitHub workflow is stale: it does not build LightingShowcase.Windows.sln."
fi

if grep -Fq 'LightingShowcase.sln' .github/workflows/dotnet-desktop.yml; then
    fail "The GitHub workflow still references the retired LightingShowcase.sln."
fi

if grep -R -Fq 'validate-platform-split.sh' .github/workflows; then
    fail "A GitHub workflow still invokes the retired validate-platform-split.sh script."
fi

if [[ ! -d .git ]]; then
    log "This is a downloaded source folder, so a local Git repository will be created."
    git init
    git branch -M "$BRANCH"
    git remote add origin "$REPOSITORY_URL"

    log "Fetching the existing GitHub branch without replacing downloaded files."
    if ! git fetch origin "$BRANCH"; then
        fail "Could not fetch origin/$BRANCH. Check the repository URL, branch name, network connection, and GitHub authentication."
    fi

    # Attach the downloaded working tree to the existing remote history. A mixed
    # reset updates HEAD and the index but deliberately leaves every downloaded
    # file in the working directory unchanged.
    git reset --mixed "origin/$BRANCH"
else
    log "Existing local Git metadata found."

    if git remote get-url origin >/dev/null 2>&1; then
        CURRENT_REMOTE="$(git remote get-url origin)"
        if [[ "$CURRENT_REMOTE" != "$REPOSITORY_URL" ]]; then
            log "Changing origin from $CURRENT_REMOTE to $REPOSITORY_URL"
            git remote set-url origin "$REPOSITORY_URL"
        fi
    else
        git remote add origin "$REPOSITORY_URL"
    fi

    CURRENT_BRANCH="$(git branch --show-current)"
    if [[ -n "$CURRENT_BRANCH" && "$CURRENT_BRANCH" != "$BRANCH" ]]; then
        fail "The current branch is '$CURRENT_BRANCH', not '$BRANCH'. Switch branches or pass '$CURRENT_BRANCH' as the second argument."
    fi

    git branch -M "$BRANCH"

    log "Fetching the latest GitHub history."
    if ! git fetch origin "$BRANCH"; then
        fail "Could not fetch origin/$BRANCH. Check the repository URL, branch name, network connection, and GitHub authentication."
    fi
fi

# Git requires an author identity before it can create a commit. Prompt only when
# the machine does not already have one configured.
if ! git config user.name >/dev/null 2>&1; then
    read -r -p "Git commit name: " GIT_NAME
    [[ -n "$GIT_NAME" ]] || fail "A Git commit name is required."
    git config user.name "$GIT_NAME"
fi

if ! git config user.email >/dev/null 2>&1; then
    read -r -p "Git commit email (a GitHub noreply address is acceptable): " GIT_EMAIL
    [[ -n "$GIT_EMAIL" ]] || fail "A Git commit email is required."
    git config user.email "$GIT_EMAIL"
fi

log "Staging source changes. Build output remains excluded by .gitignore."
git add -A

if git diff --cached --quiet; then
    log "No changed files need to be committed."
else
    printf '\nFiles that will be committed:\n'
    git status --short

    printf '\nCommit message: %s\n' "$COMMIT_MESSAGE"
    read -r -p "Commit and push these files to GitHub? [y/N] " CONFIRM
    case "$CONFIRM" in
        y|Y|yes|YES)
            ;;
        *)
            fail "Cancelled. No commit was created and nothing was pushed."
            ;;
    esac

    git commit -m "$COMMIT_MESSAGE"
fi

# Incorporate commits that may have reached GitHub since the ZIP was downloaded.
# Rebase keeps the upload commit on top of the current remote branch.
if git show-ref --verify --quiet "refs/remotes/origin/$BRANCH"; then
    log "Rebasing the local commit onto the latest origin/$BRANCH."
    if ! git rebase "origin/$BRANCH"; then
        printf '\nA rebase conflict occurred. Resolve the files and run:\n' >&2
        printf '  git add <resolved-files>\n' >&2
        printf '  git rebase --continue\n' >&2
        printf '  git push -u origin %q\n' "$BRANCH" >&2
        printf '\nTo abandon the rebase, run: git rebase --abort\n' >&2
        exit 1
    fi
fi

log "Pushing $BRANCH to GitHub."
if ! git push -u origin "$BRANCH"; then
    printf '\nPush failed. GitHub does not accept account passwords for Git operations.\n' >&2
    printf 'Authenticate with one of these methods, then rerun the script:\n' >&2
    printf '  gh auth login\n' >&2
    printf '  or configure an SSH remote / personal access token.\n' >&2
    exit 1
fi

log "GitHub was updated successfully."
git status --short --branch
