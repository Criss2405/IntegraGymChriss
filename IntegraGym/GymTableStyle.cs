using System.Runtime.CompilerServices;
namespace IntegraGym;

internal static class GymTableStyle
{
    private static readonly ConditionalWeakTable<DataGridView,object> Styled=new();

    internal static void Apply(DataGridView g)
    {
        g.BackgroundColor=DashboardArt.Panel;g.BorderStyle=BorderStyle.None;
        g.GridColor=Night.Line;g.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
        g.RowHeadersVisible=false;g.EnableHeadersVisualStyles=false;
        g.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight=g.LogicalToDeviceUnits(42);g.RowTemplate.Height=g.LogicalToDeviceUnits(43);
        var header=g.ColumnHeadersDefaultCellStyle;
        header.BackColor=header.SelectionBackColor=DashboardArt.Panel;
        header.ForeColor=header.SelectionForeColor=Night.Blue;
        header.Alignment=DataGridViewContentAlignment.MiddleLeft;header.Padding=new Padding(8,0,8,0);
        var row=g.DefaultCellStyle;row.BackColor=DashboardArt.Panel;row.ForeColor=Color.White;
        row.SelectionBackColor=Night.Selected;row.SelectionForeColor=Color.White;row.Padding=new Padding(8,3,8,3);
        g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(12,33,57);
        DashboardLook.ApplyGrid(g);
        if(Styled.TryGetValue(g,out _))return;
        Styled.Add(g,new object());
        g.CellPainting+=DashboardArt.Badge;
        DashboardArt.DarkScroll(g);foreach(Control bar in g.Controls)if(bar is ScrollBar)DashboardArt.DarkScroll(bar);
    }
}



