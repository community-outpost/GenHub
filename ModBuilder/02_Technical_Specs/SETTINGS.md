## Configuration settings

### Bundle Items

| Setting                                           | Mandatory | Default | Description                                                                                                         |
|---------------------------------------------------|-----------|---------|---------------------------------------------------------------------------------------------------------------------|
| bundles.version                                   | no        | 1       | json Format version                                                                                                 |
| bundles.itemsPrefix                               | no        |         | A prefix added to all generated .big file names                                                                     |
| bundles.itemsSuffix                               | no        |         | A suffix added to all generated .big file names                                                                     |
| bundles.items                                     | no        |         | Item list                                                                                                           |
| bundles.items[].name                              | yes       |         | Item name                                                                                                           |
| bundles.items[].big                               | no        | True    | Item is a .big file?                                                                                                |
| bundles.items[].files                             | no        |         | Item file list                                                                                                      |
| bundles.items[].files[].parent                    | no        |         | Source file(s) parent folder                                                                                        |
| bundles.items[].files[].source                    | no        |         | Source file(s), accepts wild cards \*.\* or A.\* or \*.B                                                            |
| bundles.items[].files[].target                    | no        |         | Target file(s), accepts wild cards \*.\* or A.\* or \*.B                                                            |
| bundles.items[].files[].params                    | no        |         | File params, see Sample Project for examples                                                                        |
| bundles.items[].files[].sourceList                | no        |         | List of source file(s), target file is automatic, alternative to 'source', accepts wild cards \*.\* or A.\* or \*.B |
| bundles.items[].files[].sourceTargetList          | no        |         | List of source and target file(s), alternative to 'source' and 'target', accepts wild cards \*.\* or A.\* or \*.B   |
| bundles.items[].files[].sourceTargetList[].source | yes       |         | Source file as part of the list                                                                                     |
| bundles.items[].files[].sourceTargetList[].target | yes       |         | Target file as part of the list                                                                                     |
| bundles.items[].files[].sourceTargetList[].params | no        |         | Not implemented                                                                                                     |
| bundles.items[].onPreBuild                        | no        |         | Special callback event that is executed before build. Used to inject custom script logic                            |
| bundles.items[].onPreBuild.script                 | yes       |         | Python script called on event                                                                                       |
| bundles.items[].onPreBuild.function               | no        | OnEvent | Python script function called                                                                                       |
| bundles.items[].onPreBuild.kwargs                 | no        |         | Arbitrary keyword arguments passed to Python script function                                                        |

### Bundle Packs

| Setting                             | Mandatory | Default | Description                                                                              |
|-------------------------------------|-----------|---------|------------------------------------------------------------------------------------------|
| bundles.version                     | no        | 1       | json Format version                                                                      |
| bundles.packsPrefix                 | no        |         | A prefix added to all generated .zip file names                                          |
| bundles.packsSuffix                 | no        |         | A suffix added to all generated .zip file names                                          |
| bundles.packs                       | no        |         | Pack list                                                                                |
| bundles.packs[].install             | no        | False   | Pack is installed by Mod Builder for testing?                                            |
| bundles.packs[].name                | yes       |         | Pack name                                                                                |
| bundles.packs[].itemNames           | yes       |         | Item name list                                                                           |
| bundles.packs[].onPreBuild          | no        |         | Special callback event that is executed before build. Used to inject custom script logic |
| bundles.items[].onPreBuild.script   | yes       |         | Python script called on event                                                            |
| bundles.items[].onPreBuild.function | no        | OnEvent | Python script function called                                                            |
| bundles.items[].onPreBuild.kwargs   | no        |         | Arbitrary keyword arguments passed to Python script function                             |
