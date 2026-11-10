using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// Where a folder may go: an existing parent, five levels at most counting its subfolders, never inside itself or one of its
// subfolders, and without a sibling of the same name; and the set of folders of an item, saved against the one that was read.
public sealed class FormFolderTreeTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid(),
        F = Guid.NewGuid(), G = Guid.NewGuid();

    // A > B > C > D > E is a chain of five levels; F > G is another branch from the top.
    private static readonly FormFolderNode[] Chain =
    [
        new(A, null, "A"), new(B, A, "B"), new(C, B, "C"), new(D, C, "D"), new(E, D, "E"), new(F, null, "F"), new(G, F, "G"),
    ];

    [Fact]
    public void A_folder_fits_up_to_the_fifth_level()
    {
        Assert.True(FormFolderTree.CheckPlacement(null, D, "NEW", Chain).IsSuccess);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(null, E, "NEW", Chain).Error);
        Assert.True(FormFolderTree.CheckPlacement(null, null, "NEW", Chain).IsSuccess);
    }

    [Fact]
    public void Moving_counts_the_levels_of_the_subfolders_that_go_along()
    {
        // D carries E: under F they fill levels 2 and 3, and in its own place nothing changes. B carries four levels: under G it
        // would reach the sixth.
        Assert.True(FormFolderTree.CheckPlacement(D, F, "D", Chain).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(D, C, "D", Chain).IsSuccess);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(F, E, "F", Chain).Error);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(B, G, "B", Chain).Error);
        Assert.True(FormFolderTree.CheckPlacement(C, G, "C", Chain).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(B, F, "B", Chain).IsSuccess);
    }

    [Fact]
    public void A_folder_cannot_go_inside_itself_or_one_of_its_subfolders()
    {
        Assert.Equal(FormFolderErrors.Cycle, FormFolderTree.CheckPlacement(B, B, "B", Chain).Error);
        Assert.Equal(FormFolderErrors.Cycle, FormFolderTree.CheckPlacement(B, D, "B", Chain).Error);
        Assert.True(FormFolderTree.CheckPlacement(B, null, "B", Chain).IsSuccess);
    }

    [Fact]
    public void A_sibling_with_the_same_name_is_refused_and_another_parent_is_fine()
    {
        Assert.Equal(FormFolderErrors.NameTaken, FormFolderTree.CheckPlacement(null, null, "F", Chain).Error);
        Assert.Equal(FormFolderErrors.NameTaken, FormFolderTree.CheckPlacement(B, null, "A", Chain).Error);
        Assert.True(FormFolderTree.CheckPlacement(null, A, "F", Chain).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(F, null, "F", Chain).IsSuccess);
    }

    [Fact]
    public void A_missing_parent_is_not_found()
    {
        Assert.Equal(FormFolderErrors.NotFound, FormFolderTree.CheckPlacement(null, Guid.NewGuid(), "NEW", Chain).Error);
    }

    [Fact]
    public void The_subtree_holds_the_folder_and_every_level_below()
    {
        Assert.Equal(new[] { B, C, D, E }.Order(), FormFolderTree.Subtree(B, Chain).Order());
        Assert.Equal(new[] { F, G }.Order(), FormFolderTree.Subtree(F, Chain).Order());
        Assert.Equal([E], FormFolderTree.Subtree(E, Chain));
    }

    [Fact]
    public void The_set_of_an_item_is_saved_only_against_the_one_that_was_read()
    {
        var plan = FormFolderTree.PlanMemberships([A, B], [B, A], [B, C]);

        Assert.True(plan.IsSuccess);
        Assert.Equal([C], plan.Value.Added);
        Assert.Equal([A], plan.Value.Removed);
        Assert.Equal(FormFolderErrors.SetChanged, FormFolderTree.PlanMemberships([A, B], [A], [A]).Error);
        Assert.Equal(FormFolderErrors.SetChanged, FormFolderTree.PlanMemberships([A], [A, B], [A]).Error);
        var unchanged = FormFolderTree.PlanMemberships([A], [A], [A]).Value;
        Assert.Empty(unchanged.Added);
        Assert.Empty(unchanged.Removed);
    }
}
