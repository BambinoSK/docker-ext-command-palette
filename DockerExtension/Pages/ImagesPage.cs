using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Pages;

internal sealed partial class ImagesPage : ResourceListPage<ImageSummary>
{
    private const string FilterInUse = "inuse";
    private const string FilterUnused = "unused";
    private const string FilterDangling = "dangling";

    private readonly SimpleFilters _filters;

    public ImagesPage()
        : base(DockerResource.Images)
    {
        Icon = Icons.Image;
        Title = "Docker images";
        Name = "Open";
        PlaceholderText = "Search images by repository or tag";
        EmptyContent = new CommandItem(new NoOpCommand()) { Title = "No images", Subtitle = "Nothing matches this filter", Icon = Icons.Image };
        _filters = new SimpleFilters(
            () => RaiseItemsChanged(),
            ("all", "All", Icons.All),
            (FilterInUse, "In use", Icons.Start),
            (FilterUnused, "Unused", Icons.Stop),
            (FilterDangling, "Dangling", Icons.Warning));
        Filters = _filters;
    }

    protected override async Task<IReadOnlyList<ImageSummary>> LoadAsync(DockerClient client, CancellationToken ct) =>
        await client.ListImagesAsync(ct).ConfigureAwait(false);

    protected override IListItem[] BuildItems(IReadOnlyList<ImageSummary> images)
    {
        var usage = DockerService.Instance.Containers
            .GroupBy(c => c.ImageID)
            .ToDictionary(g => g.Key, g => g.Select(c => c.DisplayName()).ToList());

        var rows = images
            .Select(i => (Image: i, Tags: RealTags(i), UsedBy: usage.GetValueOrDefault(i.Id) ?? []))
            .Where(r => _filters.CurrentFilterId switch
            {
                FilterInUse => r.UsedBy.Count > 0,
                FilterUnused => r.UsedBy.Count == 0,
                FilterDangling => r.Tags.Count == 0,
                _ => true,
            })
            .OrderByDescending(r => r.Image.Created)
            .Select(r => (IListItem)BuildItem(r.Image, r.Tags, r.UsedBy))
            .ToList();

        rows.Add(CleanUp(ResourceCommands.Prune(
            "Remove dangling images",
            "dangling images",
            "Untagged images that no container uses are deleted.",
            DockerResource.Images,
            (client, ct) => client.PruneImagesAsync(allUnused: false, ct))));
        rows.Add(CleanUp(ResourceCommands.Prune(
            "Remove all unused images",
            "unused images",
            "Every image that no container uses is deleted, including tagged ones. They will be pulled again when needed.",
            DockerResource.Images,
            (client, ct) => client.PruneImagesAsync(allUnused: true, ct))));

        return [.. rows];
    }

    private static ListItem CleanUp(InvokableCommand command) =>
        new(command) { Title = command.Name, Icon = Icons.Prune, Section = "Clean up" };

    private static List<string> RealTags(ImageSummary image) =>
        image.RepoTags?.Where(t => t != "<none>:<none>").ToList() ?? [];

    private static ListItem BuildItem(ImageSummary image, List<string> repoTags, List<string> usedBy)
    {
        var shortId = Format.ShortId(image.Id);
        var reference = repoTags.FirstOrDefault();
        var title = reference ?? $"<none> ({shortId})";

        var specs = new List<CommandSpec>();
        if (reference is not null)
        {
            specs.Add(new CommandSpec(
                ResourceCommands.Terminal("Run in terminal", Icons.Shell, reference, ["run", "--rm", "-it", reference]),
                "Run in terminal",
                Icons.Shell,
                CommandSpec.Ctrl(VirtualKey.R)));
            specs.Add(new CommandSpec(
                ResourceCommands.Background("Pull latest", Icons.Pull, $"Pulling {reference}…", $"Couldn't pull {reference}", DockerResource.Images, async (client, ct) =>
                {
                    await client.PullImageAsync(reference, ct).ConfigureAwait(false);
                    return $"Pulled {reference}";
                }),
                "Pull latest",
                Icons.Pull,
                CommandSpec.Ctrl(VirtualKey.P)));
            specs.Add(new CommandSpec(new CopyTextCommand(reference) { Name = "Copy name" }, "Copy name", Icons.Copy));
        }

        specs.Add(new CommandSpec(new CopyTextCommand(image.Id) { Name = "Copy ID" }, "Copy ID", Icons.Copy, CommandSpec.CtrlShift(VirtualKey.C)));

        // Removing by tag only untags when other tags remain; a dangling image is removed by ID.
        var removeTarget = reference ?? image.Id;
        var removeTitle = repoTags.Count > 1 ? "Remove tag" : "Remove image";
        var remove = ResourceCommands.Background(removeTitle, Icons.Delete, $"Removing {title}…", $"Couldn't remove {title}", DockerResource.Images, async (client, ct) =>
        {
            await client.RemoveImageAsync(removeTarget, force: false, ct).ConfigureAwait(false);
            return $"Removed {title}";
        });
        specs.Add(new CommandSpec(
            ResourceCommands.Confirmed(remove, $"Remove {title}?", repoTags.Count > 1 ? $"Only the tag {reference} is removed; the image keeps its other tags." : "The image is deleted from this engine."),
            removeTitle,
            Icons.Delete,
            CommandSpec.Ctrl(VirtualKey.Delete),
            Critical: true));

        var subtitle = $"{Format.Bytes(image.Size)} · created {Format.Ago(image.Created)}";
        if (usedBy.Count > 0)
        {
            subtitle += $" · used by {Format.Plural(usedBy.Count, "container")}";
        }

        var tags = new List<ITag>();
        if (usedBy.Count > 0)
        {
            tags.Add(StatusItems.Colored("in use", 0x2E, 0xA0, 0x43));
        }

        if (repoTags.Count == 0)
        {
            tags.Add(new Tag("dangling"));
        }
        else if (repoTags.Count > 1)
        {
            tags.Add(new Tag($"+{repoTags.Count - 1} tags"));
        }

        var metadata = new List<IDetailsElement>
        {
            StatusItems.Text("ID", shortId),
            StatusItems.Text("Size", Format.Bytes(image.Size)),
            StatusItems.Text("Created", Format.Ago(image.Created)),
        };
        metadata.AddRange(repoTags.Select(t => StatusItems.Text("Tag", t)));
        metadata.AddRange((image.RepoDigests ?? []).Take(3).Select(d => StatusItems.Text("Digest", d)));
        if (usedBy.Count > 0)
        {
            metadata.Add(StatusItems.Text("Used by", string.Join(", ", usedBy)));
        }

        var primary = specs[0];
        return new ListItem(primary.Command)
        {
            Title = title,
            Subtitle = subtitle,
            Icon = Icons.Image,
            Tags = [.. tags],
            MoreCommands = CommandSpec.ToContextItems(specs.Skip(1)),
            Details = new Details { Title = title, Metadata = [.. metadata] },
        };
    }
}
