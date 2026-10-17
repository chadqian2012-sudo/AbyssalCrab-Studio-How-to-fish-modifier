from pathlib import Path
from lxml import etree as E
import json,hashlib,shutil
root=Path(__file__).resolve().parents[1]
source=root.parent/'howtofish-local-test/source/HowToFishTrainer'
target=root/'source/HowToFishTrainer'
app=target/'HowToFishTrainer/App'
previous=json.loads((root/'build/ui-agent-start-snapshot.json').read_text(encoding='utf-8'))
changes=[]
for path in source.rglob('*'):
    if not path.is_file() or {'obj','bin'}.intersection(path.relative_to(source).parts):continue
    relative=str(path.relative_to(source));digest=hashlib.sha256(path.read_bytes()).hexdigest()
    if previous.get(relative)!=digest:changes.append({'file':relative,'before':previous.get(relative),'after':digest})
allowed={'app.ico','assets\\brand\\liyu-app-icon.png','assets\\brand\\abyssalcrab-app-icon.png','HowToFishTrainer.csproj','HowToFishTrainer\\App\\MainWindow.xaml','HowToFishTrainer\\App\\ActivationWindow.xaml','themes\\theme.xaml'}
assert {item['file'] for item in changes}<=allowed, 'Unexpected non-UI agent changes require review: '+str(changes)
def brand(text):
    return text.replace('\u7483\u8bed\u4fee\u6539\u5668','AbyssalCrab Studio').replace('\u7483\u8bed\u6fc0\u6d3b\u4e2d\u5fc3','AbyssalCrab Studio').replace('liyu-app-icon','abyssalcrab-app-icon').replace('LIYU-','ACST-').replace('liyu-','acst-')
for relative in ['themes/theme.xaml','HowToFishTrainer/App/ActivationWindow.xaml']:
    text=brand((source/relative).read_text(encoding='utf-8-sig'))
    (target/relative).write_text(text,encoding='utf-8')
shutil.copy2(source/'app.ico',target/'app.ico')
source_icon=source/'assets/brand/abyssalcrab-app-icon.png'
if not source_icon.exists():source_icon=source/'assets/brand/liyu-app-icon.png'
shutil.copy2(source_icon,target/'assets/brand/abyssalcrab-app-icon.png')
wpf='http://schemas.microsoft.com/winfx/2006/xaml/presentation';x='http://schemas.microsoft.com/winfx/2006/xaml'
ns={'w':wpf,'x':x}
doc=E.fromstring(brand((source/'HowToFishTrainer/App/MainWindow.xaml').read_text(encoding='utf-8-sig')).encode('utf-8'))
nav=doc.xpath('.//w:ListBox[@ItemsSource="{Binding Pages}"]',namespaces=ns)[0]
main=nav.getparent().getparent().getparent()
content=main.xpath('./w:Grid[@Column="1"]',namespaces=ns)[0]
main.remove(main.find('{'+wpf+'}Grid.ColumnDefinitions'))
side=nav.getparent().getparent();side.getparent().remove(side)
rows=E.Element('{'+wpf+'}Grid.RowDefinitions')
for height in ('Auto','*'):E.SubElement(rows,'{'+wpf+'}RowDefinition',Height=height)
main.insert(0,rows);nav.getparent().remove(nav)
nav.set('{'+x+'}Name','TopCategories');nav.set('Margin','8,6')
nav.set('ScrollViewer.VerticalScrollBarVisibility','Disabled');nav.set('ScrollViewer.HorizontalScrollBarVisibility','Disabled')
panel=E.Element('{'+wpf+'}ItemsControl.ItemsPanel');template=E.SubElement(panel,'{'+wpf+'}ItemsPanelTemplate');E.SubElement(template,'{'+wpf+'}WrapPanel',Orientation='Horizontal');nav.insert(0,panel)
nav.attrib.pop('ItemContainerStyle',None)
container=E.Element('{'+wpf+'}ItemsControl.ItemContainerStyle');style=E.SubElement(container,'{'+wpf+'}Style',TargetType='{x:Type ListBoxItem}',BasedOn='{StaticResource NavListItem}')
E.SubElement(style,'{'+wpf+'}Setter',Property='Padding',Value='14,10');E.SubElement(style,'{'+wpf+'}Setter',Property='Margin',Value='0,1');nav.insert(0,container)
top=E.Element('{'+wpf+'}Border',Background='{StaticResource PanelBrush}',BorderBrush='{StaticResource LineBrush}',BorderThickness='0,0,0,1');top.append(nav);main.insert(1,top)
content.attrib.pop('Column',None);content.set('Grid.Row','1');content.set('Margin','24,18,18,16')
for text in doc.xpath('.//w:TextBlock',namespaces=ns):
    if text.get('Text') in ('联机安全','运行时确认模式','{x:Static app:MainViewModel.ConsentWarning}','{x:Static app:MainViewModel.ConnectionWarning}'):
        parent=text.getparent()
        while parent is not None and parent.tag!='{'+wpf+'}Border':parent=parent.getparent()
        if parent is not None and parent.getparent() is not None:parent.getparent().remove(parent)
for button in doc.xpath('.//w:Button[@Click="LicenseBadge_Click"]',namespaces=ns):button.getparent().remove(button)
mode=doc.xpath('.//w:TextBlock[@Text="{Binding ModeStatus}"]',namespaces=ns)[0]
header=mode.getparent();cols=header.find('{'+wpf+'}Grid.ColumnDefinitions');cols.remove(cols[2]);mode.set('Grid.Column','2')
title=doc.xpath('.//w:TextBlock[@Text="{x:Static app:ProductIdentity.ProductName}"]',namespaces=ns)[0]
title_grid=title.getparent().getparent().getparent().getparent()
title_grid.find('{'+wpf+'}Grid.ColumnDefinitions')[0].set('Width','290')
E.ElementTree(doc).write(str(app/'MainWindow.xaml'),encoding='utf-8',xml_declaration=True,pretty_print=True)
theme=(target/'themes/theme.xaml').read_text(encoding='utf-8')
assert '#466E7D' in theme and 'ContentSource="Content"' in theme
runtime=target/'HowToFishTrainer.Runtime.dll';assert hashlib.sha256(runtime.read_bytes()).hexdigest()=='ca7b4bd70d7ac5c80eb98e9207f894aaa511e195091a26e9cb38425046d78393'
info={'ui_agent_changes':changes,'theme_color':'#466E7D','cover_sha256':hashlib.sha256((target/'assets/brand/abyssalcrab-app-icon.png').read_bytes()).hexdigest(),'icon_sha256':hashlib.sha256((target/'app.ico').read_bytes()).hexdigest(),'new_runtime_sha256':hashlib.sha256(runtime.read_bytes()).hexdigest(),'top_navigation_retained':True,'source_hashes':{str(p.relative_to(target)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [target/'themes/theme.xaml',app/'MainWindow.xaml',app/'ActivationWindow.xaml']}}
(root/'build/final-ui-merge.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8')
print('Merged',len(changes),'UI agent files; #466E7D, new cover, top navigation, latest runtime preserved')
