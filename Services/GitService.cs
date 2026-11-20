using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NoteDraw.Services
{
    public sealed record GitFileChange(string StatusCode, string RelativePath, string StatusDescription);

    public sealed record GitCommitInfo(string ShortHash, string Subject, string RelativeTime, string Author);

    public sealed class GitRepositoryStatus
    {
        public bool IsGitAvailable { get; init; }
        public bool IsRepository { get; init; }
        public string BranchName { get; init; } = string.Empty;
        public string RemoteUrl { get; init; } = string.Empty;
        public IReadOnlyList<GitFileChange> Changes { get; init; } = Array.Empty<GitFileChange>();
        public IReadOnlyList<GitCommitInfo> RecentCommits { get; init; } = Array.Empty<GitCommitInfo>();
    }

    public sealed record GitOperationResult(bool Success, string Output);

    /// <summary>
    /// Integrates local vault version control with the system `git` CLI
    /// (status, init, stage/commit, diff, branch switching, remote config, pull, push).
    /// </summary>
    public sealed class GitService
    {
        public bool IsGitInstalled()
        {
            var res = RunGit(Environment.CurrentDirectory, "--version");
            return res.ExitCode == 0;
        }

        public GitRepositoryStatus GetStatus(string vaultPath)
        {
            if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
            {
                return new GitRepositoryStatus
                {
                    IsGitAvailable = IsGitInstalled(),
                    IsRepository = false
                };
            }

            if (!IsGitInstalled())
            {
                return new GitRepositoryStatus
                {
                    IsGitAvailable = false,
                    IsRepository = false
                };
            }

            var inside = RunGit(vaultPath, "rev-parse --is-inside-work-tree");
            if (inside.ExitCode != 0 || !inside.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return new GitRepositoryStatus
                {
                    IsGitAvailable = true,
                    IsRepository = false
                };
            }

            string branch = "HEAD";
            var branchRes = RunGit(vaultPath, "symbolic-ref --short HEAD");
            if (branchRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(branchRes.StdOut))
            {
                branch = branchRes.StdOut.Trim();
            }
            else
            {
                var altBranch = RunGit(vaultPath, "rev-parse --abbrev-ref HEAD");
                if (altBranch.ExitCode == 0 && !string.IsNullOrWhiteSpace(altBranch.StdOut))
                    branch = altBranch.StdOut.Trim();
            }

            string remoteUrl = string.Empty;
            var remoteRes = RunGit(vaultPath, "remote get-url origin");
            if (remoteRes.ExitCode == 0)
                remoteUrl = remoteRes.StdOut.Trim();

            var changes = new List<GitFileChange>();
            var statusRes = RunGit(vaultPath, "status --porcelain");
            if (statusRes.ExitCode == 0 && !string.IsNullOrEmpty(statusRes.StdOut))
            {
                foreach (string rawLine in statusRes.StdOut.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (rawLine.Length < 4)
                        continue;

                    string code = rawLine[..2].Trim();
                    string relPath = rawLine[3..].Trim().Trim('"');
                    int arrowIdx = relPath.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrowIdx >= 0)
                        relPath = relPath[(arrowIdx + 4)..].Trim().Trim('"');

                    changes.Add(new GitFileChange(code, relPath, DescribeStatusCode(code)));
                }
            }

            var commits = new List<GitCommitInfo>();
            var logRes = RunGit(vaultPath, "log -n 12 --pretty=format:\"%h%x1f%s%x1f%cr%x1f%an\"");
            if (logRes.ExitCode == 0 && !string.IsNullOrEmpty(logRes.StdOut))
            {
                foreach (string line in logRes.StdOut.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('\x1f');
                    if (parts.Length >= 4)
                    {
                        commits.Add(new GitCommitInfo(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
                    }
                }
            }

            return new GitRepositoryStatus
            {
                IsGitAvailable = true,
                IsRepository = true,
                BranchName = branch,
                RemoteUrl = remoteUrl,
                Changes = changes,
                RecentCommits = commits
            };
        }

        public GitOperationResult InitRepository(string vaultPath)
        {
            if (string.IsNullOrWhiteSpace(vaultPath))
                return new GitOperationResult(false, "Vault path is empty.");

            Directory.CreateDirectory(vaultPath);
            var initRes = RunGit(vaultPath, "init");
            if (initRes.ExitCode != 0)
                return new GitOperationResult(false, CombineOutput(initRes));

            string gitignorePath = Path.Combine(vaultPath, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                File.WriteAllText(
                    gitignorePath,
                    ".obsidian/workspace.json\n.obsidian/workspace-mobile.json\n.DS_Store\nThumbs.db\n");
            }

            return new GitOperationResult(true, string.IsNullOrWhiteSpace(initRes.StdOut) ? "Initialized Git repository." : initRes.StdOut.Trim());
        }

        public GitOperationResult StageAndCommitAll(string vaultPath, string message)
        {
            if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
                return new GitOperationResult(false, "Active vault directory does not exist.");

            string cleanMsg = string.IsNullOrWhiteSpace(message)
                ? $"NoteDraw update ({DateTime.Now:yyyy-MM-dd HH:mm})"
                : message.Trim();

            EnsureCommitIdentity(vaultPath);

            var addRes = RunGit(vaultPath, "add -A");
            if (addRes.ExitCode != 0)
                return new GitOperationResult(false, CombineOutput(addRes));

            var commitRes = RunGitWithArgs(vaultPath, new[] { "commit", "-m", cleanMsg });
            if (commitRes.ExitCode != 0)
            {
                string combined = CombineOutput(commitRes);
                bool nothingToCommit = combined.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase);
                return new GitOperationResult(nothingToCommit, string.IsNullOrWhiteSpace(combined) ? "Nothing to commit." : combined);
            }

            return new GitOperationResult(true, CombineOutput(commitRes));
        }

        public GitOperationResult Pull(string vaultPath)
        {
            var res = RunGit(vaultPath, "pull");
            return new GitOperationResult(res.ExitCode == 0, CombineOutput(res));
        }

        public GitOperationResult Push(string vaultPath)
        {
            var res = RunGit(vaultPath, "push");
            if (res.ExitCode == 0)
                return new GitOperationResult(true, string.IsNullOrWhiteSpace(res.StdOut) && string.IsNullOrWhiteSpace(res.StdErr) ? "Push completed." : CombineOutput(res));

            string combined = CombineOutput(res);
            if (combined.Contains("no upstream branch", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("set-upstream", StringComparison.OrdinalIgnoreCase))
            {
                var status = GetStatus(vaultPath);
                string branch = string.IsNullOrWhiteSpace(status.BranchName) ? "main" : status.BranchName;
                var upRes = RunGitWithArgs(vaultPath, new[] { "push", "-u", "origin", branch });
                return new GitOperationResult(upRes.ExitCode == 0, CombineOutput(upRes));
            }

            return new GitOperationResult(false, combined);
        }

        public GitOperationResult CommitAndPush(string vaultPath, string message)
        {
            var commitRes = StageAndCommitAll(vaultPath, message);
            if (!commitRes.Success)
                return commitRes;

            var pushRes = Push(vaultPath);
            string merged = $"{commitRes.Output}\n\n--- Push ---\n{pushRes.Output}".Trim();
            return new GitOperationResult(pushRes.Success, merged);
        }

        public GitOperationResult SetRemoteOrigin(string vaultPath, string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
                return new GitOperationResult(false, "Remote URL cannot be empty.");

            string cleanUrl = remoteUrl.Trim();
            var existing = RunGit(vaultPath, "remote get-url origin");
            var res = existing.ExitCode == 0
                ? RunGitWithArgs(vaultPath, new[] { "remote", "set-url", "origin", cleanUrl })
                : RunGitWithArgs(vaultPath, new[] { "remote", "add", "origin", cleanUrl });

            return new GitOperationResult(res.ExitCode == 0, res.ExitCode == 0 ? $"Remote 'origin' set to {cleanUrl}" : CombineOutput(res));
        }

        public GitOperationResult SwitchOrCreateBranch(string vaultPath, string branchName, bool createNew)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return new GitOperationResult(false, "Branch name cannot be empty.");

            string clean = branchName.Trim();
            var res = createNew
                ? RunGitWithArgs(vaultPath, new[] { "checkout", "-b", clean })
                : RunGitWithArgs(vaultPath, new[] { "checkout", clean });

            if (res.ExitCode != 0 && !createNew)
                res = RunGitWithArgs(vaultPath, new[] { "checkout", "-b", clean });

            return new GitOperationResult(res.ExitCode == 0, CombineOutput(res));
        }

        public string GetDiff(string vaultPath, string? relativePath = null)
        {
            if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
                return string.Empty;

            var args = string.IsNullOrWhiteSpace(relativePath)
                ? new[] { "diff" }
                : new[] { "diff", "--", relativePath };
            var res = RunGitWithArgs(vaultPath, args);
            if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StdOut))
                return res.StdOut;

            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                string full = Path.Combine(vaultPath, relativePath);
                if (File.Exists(full))
                {
                    try
                    {
                        return $"[Untracked / New File: {relativePath}]\n\n" + File.ReadAllText(full);
                    }
                    catch
                    {
                        return $"[Binary or unreadable file: {relativePath}]";
                    }
                }
            }

            return string.IsNullOrWhiteSpace(res.StdErr) ? "(No diff output)" : res.StdErr;
        }

        private static void EnsureCommitIdentity(string vaultPath)
        {
            var nameRes = RunGit(vaultPath, "config user.name");
            if (nameRes.ExitCode != 0 || string.IsNullOrWhiteSpace(nameRes.StdOut))
                RunGitWithArgs(vaultPath, new[] { "config", "user.name", "NoteDraw User" });

            var emailRes = RunGit(vaultPath, "config user.email");
            if (emailRes.ExitCode != 0 || string.IsNullOrWhiteSpace(emailRes.StdOut))
                RunGitWithArgs(vaultPath, new[] { "config", "user.email", "notedraw@local" });
        }

        private static string DescribeStatusCode(string code) => code switch
        {
            "M" => "Modified",
            "A" => "Added",
            "D" => "Deleted",
            "R" => "Renamed",
            "??" => "Untracked",
            "MM" => "Modified",
            _ => string.IsNullOrEmpty(code) ? "Changed" : code
        };

        private static string CombineOutput((int ExitCode, string StdOut, string StdErr) res)
        {
            string o = res.StdOut.Trim();
            string e = res.StdErr.Trim();
            if (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(e))
                return $"{o}\n{e}";
            return !string.IsNullOrEmpty(o) ? o : e;
        }

        private static (int ExitCode, string StdOut, string StdErr) RunGit(string workingDir, string rawArguments)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = rawArguments,
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };
                proc.Start();
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(15000))
                {
                    try { proc.Kill(); } catch { }
                    return (-1, stdout, "Git command timed out.");
                }
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        private static (int ExitCode, string StdOut, string StdErr) RunGitWithArgs(string workingDir, IReadOnlyList<string> args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                foreach (string a in args)
                    psi.ArgumentList.Add(a);

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(15000))
                {
                    try { proc.Kill(); } catch { }
                    return (-1, stdout, "Git command timed out.");
                }
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }
    }
}
