using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LocalRack.Models;

/// <summary>One service in a group's start order, plus how long to wait before moving on to the next one.</summary>
public sealed class GroupMember
{
    public Guid ProjectId { get; set; }

    /// <summary>
    /// The service to start. <see cref="Guid.Empty"/> only appears in groups saved before groups could order
    /// individual services; <see cref="ProjectGroup.ExpandProjectMembers"/> turns those into one member per service.
    /// </summary>
    public Guid ServiceId { get; set; }

    /// <summary>Seconds to wait after starting this service before starting the next (0 = no wait).</summary>
    public int DelaySeconds { get; set; }
}

/// <summary>An ordered set of services (from any projects) that can be started and stopped together.</summary>
public sealed class ProjectGroup : INotifyPropertyChanged
{
    public const int MaxDelaySeconds = 600;

    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    /// <summary>Services in the order they are started. Stopping runs in the reverse order.</summary>
    public List<GroupMember> Members { get; set; } = new();

    /// <summary>
    /// Replaces legacy whole-project members with one member per service of that project, in the project's own
    /// order. The wait that followed the project now follows its last service. Returns true if anything changed.
    /// </summary>
    public bool ExpandProjectMembers(IReadOnlyList<Project> projects)
    {
        if (Members.All(m => m.ServiceId != Guid.Empty)) return false;

        var expanded = new List<GroupMember>();
        foreach (var member in Members)
        {
            if (member.ServiceId != Guid.Empty)
            {
                expanded.Add(member);
                continue;
            }

            var project = projects.FirstOrDefault(p => p.Id == member.ProjectId);
            if (project is null) continue; // project was deleted

            for (var i = 0; i < project.Services.Count; i++)
            {
                var last = i == project.Services.Count - 1;
                expanded.Add(new GroupMember
                {
                    ProjectId = project.Id,
                    ServiceId = project.Services[i].Id,
                    DelaySeconds = last ? member.DelaySeconds : 0
                });
            }
        }

        Members = expanded;
        return true;
    }

    /// <summary>Resolves the members to live services, skipping any whose project or service no longer exists.</summary>
    public List<(GroupMember Member, Project Project, Service Service)> Resolve(IReadOnlyList<Project> projects)
    {
        var result = new List<(GroupMember, Project, Service)>();
        foreach (var member in Members)
        {
            var project = projects.FirstOrDefault(p => p.Id == member.ProjectId);
            var service = project?.Services.FirstOrDefault(s => s.Id == member.ServiceId);
            if (project is not null && service is not null) result.Add((member, project, service));
        }
        return result;
    }

    private string? _progress;

    /// <summary>Non-null while the group is being started, e.g. "Starting API (2 of 3)".</summary>
    [JsonIgnore]
    public string? Progress
    {
        get => _progress;
        internal set => SetField(ref _progress, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
