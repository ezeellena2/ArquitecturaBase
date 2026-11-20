using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// Where a folder may go (CM1 to CM6): an existing parent, two levels at most counting its subfolders (a menu and its groups), never
// inside itself or one of its subfolders, and without a sibling of the same name; and the set of folders of an item, saved against
// the one that was read.
public sealed class FormFolderTreeTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), F = Guid.NewGuid(), G = Guid.NewGuid();

    // A > B and A > C are a menu with two groups; F > G is another menu with one group.
    private static readonly FormFolderNode[] Tree =
    [
        new(A, null, "A"), new(B, A, "B"), new(C, A, "C"), new(F, null, "F"), new(G, F, "G"),
    ];

    [Fact]
    public void A_folder_fits_in_the_first_or_the_second_level_and_never_in_a_third()
    {
        Assert.True(FormFolderTree.CheckPlacement(null, null, "NEW", Tree).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(null, A, "NEW", Tree).IsSuccess);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(null, B, "NEW", Tree).Error);
    }

    [Fact]
    public void A_folder_with_subfolders_cannot_go_inside_another_and_a_subfolder_moves_freely()
    {
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(A, F, "A", Tree).Error);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(F, A, "F", Tree).Error);
        Assert.Equal(FormFolderErrors.TooDeep, FormFolderTree.CheckPlacement(B, G, "B", Tree).Error);
        Assert.True(FormFolderTree.CheckPlacement(B, F, "B", Tree).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(B, null, "B", Tree).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(A, null, "A", Tree).IsSuccess);
    }

    [Fact]
    public void A_folder_cannot_go_inside_itself_or_one_of_its_subfolders()
    {
        Assert.Equal(FormFolderErrors.Cycle, FormFolderTree.CheckPlacement(B, B, "B", Tree).Error);
        Assert.Equal(FormFolderErrors.Cycle, FormFolderTree.CheckPlacement(A, B, "A", Tree).Error);
    }

    [Fact]
    public void A_sibling_with_the_same_name_is_refused_and_another_parent_is_fine()
    {
        Assert.Equal(FormFolderErrors.NameTaken, FormFolderTree.CheckPlacement(null, null, "F", Tree).Error);
        Assert.Equal(FormFolderErrors.NameTaken, FormFolderTree.CheckPlacement(B, null, "A", Tree).Error);
        Assert.Equal(FormFolderErrors.NameTaken, FormFolderTree.CheckPlacement(B, A, "C", Tree).Error);
        Assert.True(FormFolderTree.CheckPlacement(null, A, "F", Tree).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(F, null, "F", Tree).IsSuccess);
    }

    [Fact]
    public void A_missing_parent_is_not_found()
    {
        Assert.Equal(FormFolderErrors.NotFound, FormFolderTree.CheckPlacement(null, Guid.NewGuid(), "NEW", Tree).Error);
    }

    [Fact]
    public void The_subtree_holds_the_folder_and_its_subfolders()
    {
        Assert.Equal(new[] { A, B, C }.Order(), FormFolderTree.Subtree(A, Tree).Order());
        Assert.Equal(new[] { F, G }.Order(), FormFolderTree.Subtree(F, Tree).Order());
        Assert.Equal([B], FormFolderTree.Subtree(B, Tree));
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

    [Fact]
    public void A_new_folder_is_refused_at_the_cap_but_a_move_is_not()
    {
        var nodes = Enumerable.Range(0, BusinessFormFolder.MaxFolders).Select(index => new FormFolderNode(Guid.NewGuid(), null, "N" + index)).ToArray();
        Assert.Equal(FormFolderErrors.TooMany, FormFolderTree.CheckPlacement(null, null, "NEW", nodes).Error);
        Assert.True(FormFolderTree.CheckPlacement(nodes[0].Id, null, "N0", nodes).IsSuccess);
        Assert.True(FormFolderTree.CheckPlacement(null, null, "NEW", nodes[1..]).IsSuccess);
    }

    [Fact]
    public void A_children_index_built_once_gives_the_same_subtrees()
    {
        var children = FormFolderTree.Children(Tree);
        Assert.Equal(new[] { A, B, C }.Order(), FormFolderTree.Subtree(A, children).Order());
        Assert.Equal(new[] { F, G }.Order(), FormFolderTree.Subtree(F, children).Order());
    }
}
