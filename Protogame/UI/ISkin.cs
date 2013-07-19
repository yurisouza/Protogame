using Microsoft.Xna.Framework;

namespace Protogame
{
    public interface ISkin
    {
        void DrawCanvas(IRenderContext context, Rectangle layout, Canvas canvas);
        void DrawFixedContainer(IRenderContext context, Rectangle layout, FixedContainer fixedContainer);
        void DrawButton(IRenderContext context, Rectangle layout, Button button);
        void DrawLabel(IRenderContext context, Rectangle layout, Label label);
        void DrawLink(IRenderContext context, Rectangle layout, Link link);
        void DrawVerticalContainer(IRenderContext context, Rectangle layout, VerticalContainer verticalContainer);
        void DrawHorizontalContainer(IRenderContext context, Rectangle layout, HorizontalContainer horizontalContainer);
        void DrawMenuItem(IRenderContext context, Rectangle layout, MenuItem menuItem);
        void DrawMenuList(IRenderContext context, Rectangle layout, MenuItem menuItem);
        void DrawMainMenu(IRenderContext context, Rectangle layout, MainMenu mainMenu);
        void DrawTreeView(IRenderContext context, Rectangle layout, TreeView treeView);
        void DrawTreeItem(IRenderContext context, Rectangle layout, TreeItem treeItem);
        void DrawSingleContainer(IRenderContext context, Rectangle layout, SingleContainer singleContainer);
        void DrawTextBox(IRenderContext context, Rectangle layout, TextBox textBox);

        int HeightForTreeItem { get; }
        int MainMenuHorizontalPadding { get; }
        int AdditionalMenuItemWidth { get; }
        int MenuItemHeight { get; }
    }
}

