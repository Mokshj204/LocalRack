using LocalRack.Models;

namespace LocalRack.Services;

/// <summary>
/// Starts and stops a group's services in order. Starting walks the members top to bottom, optionally
/// waiting after each one; stopping runs bottom to top so
/// dependents shut down before the things they depend on. All methods are called on the UI thread.
/// </summary>
public sealed class GroupRunner
{
    private readonly ServiceProcessManager _manager;
    private readonly Func<IReadOnlyList<Project>> _projects;
    private readonly Dictionary<Guid, CancellationTokenSource> _starting = new();

    public GroupRunner(ServiceProcessManager manager, Func<IReadOnlyList<Project>> projects)
    {
        _manager = manager;
        _projects = projects;
    }

    public bool IsStarting(ProjectGroup group) => _starting.ContainsKey(group.Id);

    public async Task StartAsync(ProjectGroup group)
    {
        if (_starting.ContainsKey(group.Id)) return;

        var cts = new CancellationTokenSource();
        _starting[group.Id] = cts;
        try
        {
            var members = group.Resolve(_projects());
            for (var i = 0; i < members.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();

                var (member, project, service) = members[i];
                var label = $"{project.Name} › {service.Name}";
                group.Progress = $"Starting {label} ({i + 1} of {members.Count})";
                _manager.Start(service);

                var delay = Math.Clamp(member.DelaySeconds, 0, ProjectGroup.MaxDelaySeconds);
                if (delay > 0 && i < members.Count - 1)
                {
                    for (var left = delay; left > 0; left--)
                    {
                        group.Progress = $"{label} started, next in {left}s";
                        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stop Group was pressed mid-start; StopAsync does the actual stopping.
        }
        finally
        {
            _starting.Remove(group.Id);
            cts.Dispose();
            group.Progress = null;
        }
    }

    /// <summary>Cancels a start in progress, then stops every member service in reverse order.</summary>
    public void Stop(ProjectGroup group)
    {
        Cancel(group);

        foreach (var (_, _, service) in group.Resolve(_projects()).AsEnumerable().Reverse())
        {
            _manager.Stop(service);
        }
    }

    /// <summary>Abandons a start in progress without stopping anything (used when a group is deleted).</summary>
    public void Cancel(ProjectGroup group)
    {
        if (_starting.TryGetValue(group.Id, out var cts)) cts.Cancel();
    }
}
