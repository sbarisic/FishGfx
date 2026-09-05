using System.Linq;
using System.Runtime.CompilerServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public class VoxelPageGroupingTests
{
    [Fact]
    public void InterleavedPagesKeepFirstAppearanceAndPerPageSubmissionOrder()
    {
        var pages = Enumerable.Range(0, 3).Select(_ => (VoxelGeometryPage)RuntimeHelpers.GetUninitializedObject(typeof(VoxelGeometryPage))).ToArray();
        int[] pageOrder = { 2, 0, 2, 1, 0, 2 };
        var entries = pageOrder.Select((page, i) => new VoxelPassEntry(new(pages[page], 6 * i, 6, 6, 0), new(i, 0, 0), 0)).ToArray();
        using VoxelPageGrouping grouping = new(entries);
        Assert.Equal(3, grouping.Count);
        Assert.Equal(new[] { pages[2], pages[0], pages[1] }, grouping.Pages.Take(grouping.Count));
        Assert.Equal(new[] { 3, 2, 1 }, grouping.Counts.Take(grouping.Count));
        for (int group = 0; group < grouping.Count; group++)
        {
            int previous = -1, count = 0;
            for (int index = grouping.First[group]; index >= 0; index = grouping.Next[index])
            {
                Assert.True(index > previous); previous = index; count++;
                Assert.Same(grouping.Pages[group], entries[index].Allocation.Page);
            }
            Assert.Equal(grouping.Counts[group], count);
        }
    }
}
