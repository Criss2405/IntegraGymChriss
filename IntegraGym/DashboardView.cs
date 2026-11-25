using MySqlConnector;
using System.Data;
using System.Globalization;
namespace IntegraGym;

internal sealed record DashboardMetric(string Title,string Value,string Detail,string Module);
internal sealed record DashboardData(List<DashboardMetric> Metrics,DataTable Activity,DateTime ServerTime,int[]? Attendance,Bucket[]? Plans,Bucket[]? Revenue);
internal static class DashboardService
{
 public static async Task<DashboardData> LoadAsync(int userId,CancellationToken token)
 {
  using var c=new MySqlConnection(DatabaseSettings.ConnectionString);await c.OpenAsync(token);
  var permissions=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  using(var q=new MySqlCommand("SELECT p.Codigo FROM Usuarios u JOIN Roles r ON r.RolId=u.RolId JOIN RolesPermisos rp ON rp.RolId=r.RolId JOIN Permisos p ON p.PermisoId=rp.PermisoId WHERE u.UsuarioId=@id AND u.Activo=TRUE AND r.Activo=TRUE",c))
  {q.Parameters.AddWithValue("@id",userId);using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))permissions.Add(r.GetString(0));}
  using var clock=new MySqlCommand("SELECT CURRENT_TIMESTAMP",c);DateTime now=Convert.ToDateTime(await clock.ExecuteScalarAsync(token));
  var metrics=new List<DashboardMetric>();var parts=new List<string>();
  async Task<decimal> Scalar(string sql){using var q=new MySqlCommand(sql,c);q.Parameters.AddWithValue("@day",now.Date);q.Parameters.AddWithValue("@end",now.Date.AddDays(1));return Convert.ToDecimal(await q.ExecuteScalarAsync(token));}
  Bucket[]? plans=null,revenue=null;
  if(permissions.Contains("membresias.gestionar"))
  {
   decimal n=await Scalar("SELECT COUNT(*) FROM Membresias m JOIN Clientes c ON c.ClienteId=m.ClienteId WHERE m.Estado='Activa' AND c.Estado='Activo' AND m.FechaInicio<=@day AND m.FechaVencimiento>=@day");
   metrics.Add(new("Membresías activas",n.ToString("N0"),"Clientes con plan vigente","membresias.gestionar"));
   using var breakdown=new MySqlCommand(@"SELECT p.Nombre, COUNT(*) AS cantidad FROM Membresias m
       JOIN Clientes c ON c.ClienteId=m.ClienteId JOIN Planes p ON p.PlanId=m.PlanId
       WHERE m.Estado='Activa' AND c.Estado='Activo' AND m.FechaInicio<=@day AND m.FechaVencimiento>=@day
       GROUP BY p.PlanId,p.Nombre ORDER BY cantidad DESC,p.Nombre",c);
   breakdown.Parameters.AddWithValue("@day",now.Date);var groups=new List<Bucket>();
   using(var r=await breakdown.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))groups.Add(new(r.GetString(0),r.GetInt32(1)));}
   plans=groups.Count<=4?groups.ToArray():groups.Take(3).Append(new Bucket("Otros planes",groups.Skip(3).Sum(b=>b.Value))).ToArray();
   parts.Add("SELECT c.NombreCompleto AS Cliente,CONCAT('Membresía · ',m.Estado) AS Movimiento,CAST(m.FechaInicio AS DATETIME) AS Fecha FROM Membresias m JOIN Clientes c ON c.ClienteId=m.ClienteId");
  }
  if(permissions.Contains("pagos.gestionar"))
  {
   decimal n=await Scalar("SELECT COALESCE(SUM(Importe),0) FROM Pagos WHERE Estado='Aplicado' AND FechaPago>=@day AND FechaPago<@end");
   metrics.Add(new("Ingresos del día",n.ToString("C2",CultureInfo.GetCultureInfo("es-MX")),"Pagos aplicados · MXN","pagos.gestionar"));
   var first=new DateTime(now.Year,now.Month,1).AddMonths(-5);revenue=Enumerable.Range(0,6).Select(i=>new Bucket(first.AddMonths(i).ToString("MMM yy",CultureInfo.GetCultureInfo("es-MX")),0)).ToArray();
   using var sales=new MySqlCommand(@"SELECT YEAR(FechaPago),MONTH(FechaPago),SUM(Importe) FROM Pagos WHERE Estado='Aplicado' AND FechaPago>=@first AND FechaPago<@end GROUP BY YEAR(FechaPago),MONTH(FechaPago)",c);
   sales.Parameters.AddWithValue("@first",first);sales.Parameters.AddWithValue("@end",now.Date.AddDays(1));
   using(var r=await sales.ExecuteReaderAsync(token)){while(await r.ReadAsync(token)){int i=(r.GetInt32(0)-first.Year)*12+r.GetInt32(1)-first.Month;revenue[i]=revenue[i] with {Value=r.GetDecimal(2)};}}
   parts.Add("SELECT c.NombreCompleto AS Cliente,CONCAT('Pago · ',p.Estado) AS Movimiento,p.FechaPago AS Fecha FROM Pagos p JOIN Clientes c ON c.ClienteId=p.ClienteId");
  }
  int[]? week=null;
  if(permissions.Contains("asistencias.gestionar"))
  {
   decimal n=await Scalar("SELECT COUNT(*) FROM Asistencias WHERE FechaHora>=@day AND FechaHora<@end");
   metrics.Add(new("Asistencias de hoy",n.ToString("N0"),"Entradas registradas","asistencias.gestionar"));
   week=new int[7];
   using var trend=new MySqlCommand("SELECT DATE(FechaHora),COUNT(*) FROM Asistencias WHERE FechaHora>=@start AND FechaHora<@end GROUP BY DATE(FechaHora)",c);
   var start=now.Date.AddDays(-6);trend.Parameters.AddWithValue("@start",start);trend.Parameters.AddWithValue("@end",now.Date.AddDays(1));
   using(var days=await trend.ExecuteReaderAsync(token)){while(await days.ReadAsync(token))week[(days.GetDateTime(0)-start).Days]=days.GetInt32(1);}
   parts.Add("SELECT c.NombreCompleto AS Cliente,'Entrada registrada' AS Movimiento,a.FechaHora AS Fecha FROM Asistencias a JOIN Clientes c ON c.ClienteId=a.ClienteId");
  }
  var table=new DataTable();table.Columns.Add("Cliente",typeof(string));table.Columns.Add("Movimiento",typeof(string));table.Columns.Add("Fecha",typeof(DateTime));
  if(parts.Count>0){using var q=new MySqlCommand("SELECT * FROM ("+string.Join(" UNION ALL ",parts)+") actividad ORDER BY Fecha DESC,Cliente,Movimiento LIMIT 15",c);using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))table.Rows.Add(r.GetString(0),r.GetString(1),r.GetDateTime(2));}
  return new(metrics,table,now,week,plans,revenue);
 }
}

internal sealed class DashboardView : UserControl
{
 private readonly int userId;
 private readonly CancellationTokenSource cancellation=new();
 private readonly Label status=new(){Dock=DockStyle.Fill,ForeColor=Night.Muted,AutoEllipsis=true,Text="Consultando MySQL…"};
 private readonly TableLayoutPanel cards=new(){Dock=DockStyle.Fill,RowCount=1,Margin=Padding.Empty};
 private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
 private readonly FairButton refresh=new(){Text="Actualizar",Dock=DockStyle.Right,Width=142};
 private readonly TableLayoutPanel figures=new(){Dock=DockStyle.Fill,RowCount=1,Margin=Padding.Empty};
 private readonly AttendanceTrend trend=new(){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,8)};
 private readonly TableLayoutPanel layout=new(){Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=6};
 private readonly Action<string> navigate;
 private bool loading;
 public DashboardView(int userId,Action<string> navigate)
 {
  Font=DashboardLook.Face(11);this.userId=userId;this.navigate=navigate;BackColor=DashboardLook.Canvas;AutoScroll=true;
  layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  foreach(var h in new[]{72,0,110,0,0,220})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,LogicalToDeviceUnits(h)));
  var hero=new FairHero{Dock=DockStyle.Fill,Padding=new Padding(20,12,15,12),Margin=new Padding(0,0,4,8)};
  var titleHero=new Label{Text="Tu gimnasio hoy",Font=DashboardLook.Face(22,2),Dock=DockStyle.Left,AutoSize=false,Width=254,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.White,BackColor=Color.Transparent};
  status.Font=DashboardLook.Face(11);status.ForeColor=DashboardLook.Muted;refresh.Soft=true;status.AutoEllipsis=false;status.BackColor=Color.Transparent;status.TextAlign=ContentAlignment.MiddleLeft;
  refresh.Dock=DockStyle.None;refresh.Size=new Size(142,40);var action=new Panel{Dock=DockStyle.Right,Width=146,BackColor=Color.Transparent};action.Controls.Add(refresh);action.Resize+=(_,_)=>refresh.Top=(action.Height-refresh.Height)/2;
  hero.Resize+=(_,_)=>{titleHero.Width=LogicalToDeviceUnits(hero.Width<780?216:254);titleHero.Font=DashboardLook.Face(hero.Width<780?18:22,2);};
  hero.Controls.Add(status);hero.Controls.Add(titleHero);hero.Controls.Add(action);layout.Controls.Add(hero,0,0);
  cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.Controls.Add(cards,0,2);layout.Controls.Add(figures,0,3);layout.Controls.Add(trend,0,4);
  var activity=new DashboardFrame{Dock=DockStyle.Fill,Padding=new Padding(18,9,18,12),BackColor=DashboardLook.Surface,Margin=Padding.Empty};
  AppUi.ConfigureGrid(grid);DashboardLook.ApplyGrid(grid);DashboardArt.DarkScroll(this);
  var title=new Label{Text="Actividad reciente",Dock=DockStyle.Top,Height=38,Font=DashboardLook.Face(16,2),ForeColor=Color.White};
  activity.Controls.Add(grid);activity.Controls.Add(title);layout.Controls.Add(activity,0,5);Controls.Add(layout);
  SizeChanged+=(_,_)=>ArrangePanels();
  Load+=async(_,_)=>await ReloadAsync();refresh.Click+=async(_,_)=>await ReloadAsync();
 }
 private async Task ReloadAsync()
 {
  if(loading)return;loading=true;refresh.Enabled=false;status.Text="Consultando MySQL…";
  try
  {
   var data=await DashboardService.LoadAsync(userId,cancellation.Token);if(IsDisposed)return;
   foreach(Control old in cards.Controls.Cast<Control>().ToArray())old.Dispose();cards.Controls.Clear();cards.ColumnStyles.Clear();cards.ColumnCount=Math.Max(1,data.Metrics.Count);
   for(int i=0;i<data.Metrics.Count;i++){cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/data.Metrics.Count));cards.Controls.Add(Kpi(data.Metrics[i]),i,0);}
   layout.RowStyles[2].Height=data.Metrics.Count>0?LogicalToDeviceUnits(122):0;
   ClearFigures();
   var charts=new[]{(true,data.Plans),(false,data.Revenue)}.Where(pair=>pair.Item2!=null).ToArray();
   figures.ColumnCount=Math.Max(1,charts.Length);
   foreach(var (ring,series) in charts){figures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/charts.Length));var chart=new StudioChart(ring){Dock=DockStyle.Fill,Margin=new Padding(0,4,8,6)};chart.Bind(series!);figures.Controls.Add(chart);}
   layout.RowStyles[3].Height=charts.Length==0?0:LogicalToDeviceUnits(218);
   layout.RowStyles[4].Height=data.Attendance==null?0:LogicalToDeviceUnits(data.Attendance.Any(n=>n>0)?148:48);
   trend.Visible=data.Attendance!=null;if(data.Attendance!=null)trend.SetWeek(data.ServerTime.Date,data.Attendance);
   ArrangePanels();grid.DataSource=data.Activity;if(grid.Columns["Fecha"] is DataGridViewColumn date)date.DefaultCellStyle.Format="dd/MM/yyyy HH:mm";
   status.Text=data.Metrics.Count==0?"Tu rol no tiene indicadores asignados.":$"{data.ServerTime.ToString("dd MMM yyyy",CultureInfo.GetCultureInfo("es-MX"))}   ·   Actualizado a las {data.ServerTime:HH:mm:ss}"+(data.Activity.Rows.Count==0?" · Sin movimientos todavía":"");
  }
  catch(OperationCanceledException){}
  catch(Exception ex) when(ex is MySqlException or InvalidOperationException)
  {if(!IsDisposed){foreach(Control old in cards.Controls.Cast<Control>().ToArray())old.Dispose();cards.Controls.Clear();trend.Visible=false;layout.RowStyles[2].Height=0;layout.RowStyles[3].Height=0;layout.RowStyles[4].Height=0;ClearFigures();grid.DataSource=null;status.Text="No se pudieron cargar los datos. Revisa MySQL y pulsa Actualizar.";}}
  finally{loading=false;if(!IsDisposed)refresh.Enabled=true;}
 }
 protected override void Dispose(bool disposing){if(disposing){cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
 private bool arranging;
 private void ArrangePanels()
 {
  if(arranging||layout.RowStyles.Count<6)return;arranging=true;
  try{
    void Pack(TableLayoutPanel p,int row,int minWidth,int height){
      var items=p.Controls.Cast<Control>().ToArray();
      int cols=Math.Max(1,Math.Min(items.Length, Math.Max(1,(ClientSize.Width-LogicalToDeviceUnits(18))/LogicalToDeviceUnits(minWidth))));
      int rows=(items.Length+cols-1)/cols;
      p.SuspendLayout();try{
        p.ColumnStyles.Clear();p.RowStyles.Clear();p.ColumnCount=cols;p.RowCount=Math.Max(1,rows);
        for(int k=0;k<cols;k++)p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/cols));
        for(int k=0;k<rows;k++)p.RowStyles.Add(new RowStyle(SizeType.Percent,100f/rows));
        for(int k=0;k<items.Length;k++)p.SetCellPosition(items[k],new TableLayoutPanelCellPosition(k%cols,k/cols));
        layout.RowStyles[row].Height=rows*LogicalToDeviceUnits(height);
      }finally{p.ResumeLayout();}
    }
    Pack(cards,2,305,120);Pack(figures,3,435,294);
    layout.RowStyles[5].Height=Math.Max(LogicalToDeviceUnits(216),ClientSize.Height-layout.RowStyles.Cast<RowStyle>().Take(5).Sum(style=>style.Height)-2);
  }finally{arranging=false;}
 }
 private void ClearFigures(){foreach(Control c in figures.Controls.Cast<Control>().ToArray())c.Dispose();figures.ColumnStyles.Clear();}
 private FairButton Kpi(DashboardMetric metric)
 {
  var card=new FairButton{Soft=true,Dock=DockStyle.Fill,Metric=metric,Text=metric.Title,AccessibleName=metric.Title+": "+metric.Value,AccessibleDescription="Abrir módulo",Margin=new Padding(0,4,8,8)};
  card.Click+=(_,_)=>navigate(metric.Module);return card;
 }
}













