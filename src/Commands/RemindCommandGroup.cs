using System.ComponentModel;
using System.Text;
using Boyfriend.Data;
using Boyfriend.Services;
using JetBrains.Annotations;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Extensions.Formatting;
using Remora.Rest.Core;
using Remora.Results;

namespace Boyfriend.Commands;

/// <summary>
///     Handles the command to manage reminders: /remind
/// </summary>
[UsedImplicitly]
public class RemindCommandGroup : CommandGroup
{
    private readonly ICommandContext _context;
    private readonly FeedbackService _feedback;
    private readonly GuildDataService _guildData;
    private readonly IDiscordRestUserAPI _userApi;

    public RemindCommandGroup(
        ICommandContext context, GuildDataService guildData, FeedbackService feedback,
        IDiscordRestUserAPI userApi)
    {
        _context = context;
        _guildData = guildData;
        _feedback = feedback;
        _userApi = userApi;
    }

    /// <summary>
    ///     A slash command that lists reminders of the user that called it.
    /// </summary>
    /// <returns>A feedback sending result which may or may not have succeeded.</returns>
    [Command("listremind")]
    [Description("List your reminders")]
    [DiscordDefaultDMPermission(false)]
    [RequireContext(ChannelContext.Guild)]
    [UsedImplicitly]
    public async Task<Result> ExecuteListReminderAsync()
    {
        if (!_context.TryGetContextIDs(out var guildId, out _, out var userId))
        {
            return new ArgumentInvalidError(nameof(_context), "Unable to retrieve necessary IDs from command context");
        }

        var userResult = await _userApi.GetUserAsync(userId, CancellationToken);
        if (!userResult.IsDefined(out var user))
        {
            return Result.FromError(userResult);
        }

        var data = await _guildData.GetData(guildId, CancellationToken);
        Messages.Culture = GuildSettings.Language.Get(data.Settings);

        return await ListRemindersAsync(data.GetOrCreateMemberData(userId), user, CancellationToken);
    }

    private async Task<Result> ListRemindersAsync(MemberData data, IUser user, CancellationToken ct)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < data.Reminders.Count; i++)
        {
            var reminder = data.Reminders[i];
            builder.AppendLine($"[{i}] {Markdown.InlineCode(reminder.Text)} ({Markdown.Timestamp(reminder.At)})");
        }

        var embed = new EmbedBuilder().WithSmallTitle(
                string.Format(Messages.ReminderList, user.GetTag()), user)
            .WithDescription(builder.ToString())
            .WithColour(ColorsList.Default)
            .Build();

        return await _feedback.SendContextualEmbedResultAsync(
            embed, ct);
    }

    /// <summary>
    ///     A slash command that schedules a reminder with the specified text.
    /// </summary>
    /// <param name="in">The period of time which must pass before the reminder will be sent.</param>
    /// <param name="message">The text of the reminder.</param>
    /// <returns>A feedback sending result which may or may not have succeeded.</returns>
    [Command("remind")]
    [Description("Create a reminder")]
    [DiscordDefaultDMPermission(false)]
    [RequireContext(ChannelContext.Guild)]
    [UsedImplicitly]
    public async Task<Result> ExecuteReminderAsync(
        [Description("After what period of time mention the reminder")]
        TimeSpan @in,
        [Description("Reminder message")] string message)
    {
        if (!_context.TryGetContextIDs(out var guildId, out var channelId, out var userId))
        {
            return new ArgumentInvalidError(nameof(_context), "Unable to retrieve necessary IDs from command context");
        }

        var userResult = await _userApi.GetUserAsync(userId, CancellationToken);
        if (!userResult.IsDefined(out var user))
        {
            return Result.FromError(userResult);
        }

        var data = await _guildData.GetData(guildId, CancellationToken);
        Messages.Culture = GuildSettings.Language.Get(data.Settings);

        return await AddReminderAsync(@in, message, data, channelId, user, CancellationToken);
    }

    private async Task<Result> AddReminderAsync(
        TimeSpan @in, string message, GuildData data,
        Snowflake channelId, IUser user, CancellationToken ct = default)
    {
        var remindAt = DateTimeOffset.UtcNow.Add(@in);

        data.GetOrCreateMemberData(user.ID).Reminders.Add(
            new Reminder
            {
                At = remindAt,
                Channel = channelId.Value,
                Text = message
            });

        var embed = new EmbedBuilder().WithSmallTitle(string.Format(Messages.ReminderCreated, user.GetTag()), user)
            .WithDescription(string.Format(Messages.DescriptionReminderCreated, Markdown.Timestamp(remindAt)))
            .WithColour(ColorsList.Green)
            .Build();

        return await _feedback.SendContextualEmbedResultAsync(embed, ct);
    }

    /// <summary>
    ///     A slash command that deletes a reminder using its index.
    /// </summary>
    /// <param name="index">The index of the reminder to delete.</param>
    /// <returns>A feedback sending result which may or may not have succeeded.</returns>
    [Command("delremind")]
    [Description("Delete one of your reminders")]
    [DiscordDefaultDMPermission(false)]
    [RequireContext(ChannelContext.Guild)]
    [UsedImplicitly]
    public async Task<Result> ExecuteDeleteReminderAsync(
        int index)
    {
        if (!_context.TryGetContextIDs(out var guildId, out _, out var userId))
        {
            return new ArgumentInvalidError(nameof(_context), "Unable to retrieve necessary IDs from command context");
        }

        var currentUserResult = await _userApi.GetCurrentUserAsync(CancellationToken);
        if (!currentUserResult.IsDefined(out var currentUser))
        {
            return Result.FromError(currentUserResult);
        }

        var data = await _guildData.GetData(guildId, CancellationToken);
        Messages.Culture = GuildSettings.Language.Get(data.Settings);

        return await DeleteReminderAsync(data.GetOrCreateMemberData(userId), index, currentUser, CancellationToken);
    }

    private async Task<Result> DeleteReminderAsync(MemberData data, int index, IUser currentUser, CancellationToken ct)
    {
        if (index >= data.Reminders.Count)
        {
            var failedEmbed = new EmbedBuilder().WithSmallTitle(Messages.InvalidReminderIndex, currentUser)
                .WithColour(ColorsList.Red)
                .Build();

            return await _feedback.SendContextualEmbedResultAsync(failedEmbed, ct);
        }

        data.Reminders.RemoveAt(index);

        var embed = new EmbedBuilder().WithSmallTitle(Messages.ReminderDeleted, currentUser)
            .WithColour(ColorsList.Green)
            .Build();

        return await _feedback.SendContextualEmbedResultAsync(
            embed, ct);
    }
}
