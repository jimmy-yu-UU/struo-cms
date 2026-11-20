# Third-Party Notices

StruoCMS is licensed under the **MIT License**. This file lists the third-party components that
travel inside the StruoCMS container images and are licensed under terms other than MIT, ISC, BSD
or 0BSD, together with those terms. Including them does **not** change StruoCMS's own license:
StruoCMS remains MIT.

Scope:

- API image (`Dockerfile`): the NuGet packages `dotnet publish` copies into the image, including
  indirect dependencies and the native binaries under `runtimes/`. The image carries this file at
  `/app/THIRD-PARTY-NOTICES.md`.
- Admin image (`frontend/Dockerfile`): not covered here. It carries its own file,
  `frontend/THIRD-PARTY-NOTICES.md`, at `/usr/share/licenses/struo-admin/THIRD-PARTY-NOTICES.md`.

Out of scope: the operating-system packages and the .NET runtime of the base images, which come
with their own notices, and development tools that do not travel in an image. Versions are not
repeated here; they are recorded in `Directory.Packages.props`.
The license of each component is the one its package declares, read from the package's own
metadata and license files.

## Apache License 2.0

- `AWSSDK.Core`, `AWSSDK.Extensions.Numerics`, `AWSSDK.S3` (Amazon Web Services)
- `FluentMigrator`, `FluentMigrator.Abstractions`, `FluentMigrator.Extensions.MySql`,
  `FluentMigrator.Extensions.Oracle`, `FluentMigrator.Extensions.Postgres`,
  `FluentMigrator.Extensions.SqlServer`, `FluentMigrator.Runner.Core`,
  `FluentMigrator.Runner.MySql`, `FluentMigrator.Runner.Oracle`,
  `FluentMigrator.Runner.Postgres`, `FluentMigrator.Runner.SQLite`,
  `FluentMigrator.Runner.SqlServer` (FluentMigrator project 2008-2024)
- `Serilog`, `Serilog.AspNetCore`, `Serilog.Extensions.Hosting`,
  `Serilog.Extensions.Logging`, `Serilog.Formatting.Compact`,
  `Serilog.Settings.Configuration`, `Serilog.Sinks.Console`, `Serilog.Sinks.Debug`,
  `Serilog.Sinks.File` (Serilog Contributors)
- `SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.core`, `SQLitePCLRaw.lib.e_sqlite3`,
  `SQLitePCLRaw.provider.e_sqlite3` (Copyright 2014-2024 SourceGear, LLC)
- `SqlSugarCore`, `SqlSugarCore.Dm`, `SqlSugarCore.Kdbndp` (sunkaixuan; the packages declare
  the license by URL, <http://www.apache.org/licenses/LICENSE-2.0.html>)

None of these packages ships a `NOTICE` file. The license text:

```text
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS
```

## LGPL-3.0-or-later: NetVips.Native and libvips

The native image-processing library libvips decodes, resizes and re-encodes images for the
transform endpoint (`GET /api/files/{id}/content?width=&height=&format=&fit=&quality=`). It arrives
as binaries in the `NetVips.Native` package family (one package per platform, copied under
`runtimes/<rid>/native/` in the API image), which declares the license **LGPL-3.0-or-later**.
The managed wrapper NetVips (MIT, Kleis Auke Wolthuizen) loads the binaries at runtime; StruoCMS
does not compile libvips or its source into its own assemblies.

- **Copyright**: Kleis Auke Wolthuizen (package); the libvips project (John Cupitt and
  contributors), see <https://github.com/libvips/libvips/blob/master/COPYING>.
- **License**: LGPL-3.0-or-later, <https://www.gnu.org/licenses/lgpl-3.0.html>
- **Upstream source**: <https://github.com/libvips/libvips>; packaging and build scripts for the
  binaries: <https://github.com/kleisauke/libvips-packaging>

The binaries bundle further libraries. Their licenses, as stated by the package's own
`THIRD-PARTY-NOTICES.md` (reproduced here verbatim):

> # Third-party notices

> This software contains third-party libraries
> used under the terms of the following licenses:

> | Library       | Used under the terms of                                                                                   |
> |---------------|-----------------------------------------------------------------------------------------------------------|
> | aom           | BSD 2-Clause + [Alliance for Open Media Patent License 1.0](https://aomedia.org/license/patent-license/)  |
> | cairo         | Mozilla Public License 1.1                                                                                |
> | cgif          | MIT License                                                                                               |
> | expat         | MIT License                                                                                               |
> | fontconfig    | [fontconfig License](https://gitlab.freedesktop.org/fontconfig/fontconfig/blob/main/COPYING) (BSD-like)   |
> | freetype      | [freetype License](https://git.savannah.gnu.org/cgit/freetype/freetype2.git/tree/docs/FTL.TXT) (BSD-like) |
> | fribidi       | LGPLv3                                                                                                    |
> | glib          | LGPLv3                                                                                                    |
> | harfbuzz      | MIT License                                                                                               |
> | highway       | BSD 3-Clause                                                                                              |
> | lcms          | MIT License                                                                                               |
> | libarchive    | BSD 2-Clause                                                                                              |
> | libexif       | LGPLv3                                                                                                    |
> | libffi        | MIT License                                                                                               |
> | libheif       | LGPLv3                                                                                                    |
> | libimagequant | [BSD 2-Clause](https://github.com/lovell/libimagequant/blob/main/COPYRIGHT)                               |
> | libnsgif      | MIT License                                                                                               |
> | libpng        | [libpng License](https://github.com/pnggroup/libpng/blob/master/LICENSE)                                  |
> | librsvg       | LGPLv3                                                                                                    |
> | libtiff       | [libtiff License](https://gitlab.com/libtiff/libtiff/blob/master/LICENSE.md) (BSD-like)                   |
> | libultrahdr   | MIT License                                                                                               |
> | libvips       | LGPLv3                                                                                                    |
> | libwebp       | New BSD License                                                                                           |
> | libxml2       | MIT License                                                                                               |
> | mozjpeg       | [zlib License, IJG License, BSD 3-Clause](https://github.com/mozilla/mozjpeg/blob/master/LICENSE.md)      |
> | pango         | LGPLv3                                                                                                    |
> | pixman        | MIT License                                                                                               |
> | proxy-libintl | LGPLv3                                                                                                    |
> | zlib-ng       | [zlib License](https://github.com/zlib-ng/zlib-ng/blob/develop/LICENSE.md)                                |

> Use of libraries under the terms of the LGPLv3 is via the
> "any later version" clause of the LGPLv2 or LGPLv2.1.

> Please report any errors or omissions via
> https://github.com/lovell/sharp-libvips/issues/new or
> https://github.com/kleisauke/libvips-packaging/issues/new

## Creative Commons Attribution 4.0 International

`Isopoh.Cryptography.Argon2`, `Isopoh.Cryptography.Blake2b` and
`Isopoh.Cryptography.SecureArray` (Michael Heyman, Copyright Michael Heyman 2022;
<https://github.com/mheyman/Isopoh.Cryptography.Argon2>) state in their license file:

```text
Isopoh.Cryptography.Argon2 by Michael Heyman is licensed
under a Creative Commons Attributeion 4.0 International
License, http://creativecommons.org/licenses/by/4.0/.
```

The license is at <http://creativecommons.org/licenses/by/4.0/>.

## PostgreSQL License

`Npgsql` (Copyright 2025 The Npgsql Development Team; <https://github.com/npgsql/npgsql>)
declares the license expression `PostgreSQL`, <https://licenses.nuget.org/PostgreSQL>.

## Microsoft.Data.SqlClient.SNI.runtime

`Microsoft.Data.SqlClient.SNI.runtime` (Copyright Microsoft Corporation) ships native binaries
under `runtimes/` in the API image. Its license file reads:

```text
MICROSOFT SOFTWARE LICENSE TERMS

MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY

These license terms are an agreement between you and Microsoft Corporation (or based on where you live, one of its affiliates). They apply to the software named above. The terms also apply to any Microsoft services or updates for the software, except to the extent those have different terms.

IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW.

1.  INSTALLATION AND USE RIGHTS.
    You may install and use any number of copies of the software to develop and test your applications.
2.  THIRD PARTY COMPONENTS. The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software.
3.  ADDITIONAL LICENSING REQUIREMENTS AND/OR USE RIGHTS.
    a. DISTRIBUTABLE CODE.  The software is comprised of Distributable Code. "Distributable Code" is code that you are permitted to distribute in applications you develop if you comply with the terms below.
       i. Right to Use and Distribute.
          * You may copy and distribute the object code form of the software.
          * Third Party Distribution. You may permit distributors of your applications to copy and distribute the Distributable Code as part of those applications.
      ii. Distribution Requirements. For any Distributable Code you distribute, you must
          * use the Distributable Code in your applications and not as a standalone distribution;
          * require distributors and external end users to agree to terms that protect it at least as much as this agreement; and
          * indemnify, defend, and hold harmless Microsoft from any claims, including attorneys' fees, related to the distribution or use of your applications, except to the extent that any claim is based solely on the unmodified Distributable Code.
     iii. Distribution Restrictions. You may not
          * use Microsoft's trademarks in your applications' names or in a way that suggests your applications come from or are endorsed by Microsoft; or
          * modify or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. An "Excluded License" is one that requires, as a condition of use, modification or distribution of code, that (i) it be disclosed or distributed in source code form; or (ii) others have the right to modify it.
4.  DATA.
    a. Data Collection. Some features in the software may enable collection of data from users of your applications that access or use the software. If you use these features to enable data collection in your applications, you must comply with applicable law, including getting any required user consent, and maintain a prominent privacy policy that accurately informs users about how you use, collect, and share their data. You agree to comply with all applicable provisions of the Microsoft Privacy Statement at [https://go.microsoft.com/fwlink/?LinkId=521839].
5.  SCOPE OF LICENSE. The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you may use the software only as expressly permitted in this agreement. In doing so, you must comply with any technical limitations in the software that only allow you to use it in certain ways. You may not
    * work around any technical limitations in the software;
    * reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source code for the software, except and to the extent required by third party licensing terms governing use of certain open source components that may be included in the software;
    * remove, minimize, block or modify any notices of Microsoft or its suppliers in the software;
    * use the software in any way that is against the law; or
    * share, publish, rent or lease the software, provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.
6.  EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit www.microsoft.com/exporting.
7.  SUPPORT SERVICES. Because this software is "as is," we may not provide support services for it.
8.  ENTIRE AGREEMENT. This agreement, and the terms for supplements, updates, Internet-based services and support services that you use, are the entire agreement for the software and support services.
9.  APPLICABLE LAW.  If you acquired the software in the United States, Washington law applies to interpretation of and claims for breach of this agreement, and the laws of the state where you live apply to all other claims. If you acquired the software in any other country, its laws apply.
10. CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:
    a) Australia. You have statutory guarantees under the Australian Consumer Law and nothing in this agreement is intended to affect those rights.
    b) Canada. If you acquired this software in Canada, you may stop receiving updates by turning off the automatic update feature, disconnecting your device from the Internet (if and when you re-connect to the Internet, however, the software will resume checking for and installing updates), or uninstalling the software. The product documentation, if any, may also specify how to turn off updates for your specific device or software.
    c) Germany and Austria.
       (i) Warranty. The software will perform substantially as described in any Microsoft materials that accompany it. However, Microsoft gives no contractual guarantee in relation to the software.
       (ii) Limitation of Liability. In case of intentional conduct, gross negligence, claims based on the Product Liability Act, as well as in case of death or personal or physical injury, Microsoft is liable according to the statutory law.
    Subject to the foregoing clause (ii), Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence
11. DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED "AS-IS." YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES OR CONDITIONS. TO THE EXTENT PERMITTED UNDER YOUR LOCAL LAWS, MICROSOFT EXCLUDES THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT.
12. LIMITATION ON AND EXCLUSION OF REMEDIES AND DAMAGES. YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT OR INCIDENTAL DAMAGES.
    This limitation applies to (a) anything related to the software, services, content (including code) on third party Internet sites, or third party applications; and (b) claims for breach of contract, breach of warranty, guarantee or condition, strict liability, negligence, or other tort to the extent permitted by applicable law.
    It also applies even if Microsoft knew or should have known about the possibility of the damages. The above limitation or exclusion may not apply to you because your state or country may not allow the exclusion or limitation of incidental, consequential or other damages.
```

## Oracle.ManagedDataAccess.Core

`Oracle.ManagedDataAccess.Core` (Copyright 2025, Oracle and/or its affiliates;
<https://www.oracle.com/database/technologies/appdev/dotnet.html>) is the Oracle Database driver
the Oracle backend uses. Its license file reads:

```text
Your use of this Program is governed by the Oracle Free Distribution, Hosting, and Use Terms and Conditions set forth below, unless you have received this Program (alone or as part of another Oracle product) under an Oracle license agreement (including but not limited to the Oracle Master Agreement), in which case your use of this Program is governed solely by such license agreement with Oracle.

Oracle Free Distribution, Hosting, and Use Terms and Conditions
Definitions
"Oracle" refers to Oracle America, Inc. "You" and "Your" refers to (a) a company or organization (each an "Entity") accessing the Programs, if use of the Programs will be on behalf of such Entity; or (b) an individual accessing the Programs, if use of the Programs will not be on behalf of an Entity. "Program(s)" refers to Oracle software provided by Oracle pursuant to the following terms and any updates, error corrections, and/or Program Documentation provided by Oracle. "Program Documentation" refers to Program user manuals and Program installation manuals, if any. If available, Program Documentation may be delivered with the Programs and/or may be accessed from www.oracle.com/documentation. "Separate Terms" refers to separate license terms that are specified in the Program Documentation, readmes or notice files and that apply to Separately Licensed Technology. "Separately Licensed Technology" refers to Oracle or third party technology that is licensed under Separate Terms and not under the terms of this license.

Separately Licensed Technology
Oracle may provide certain notices to You in Program Documentation, readmes or notice files in connection with Oracle or third party technology provided as or with the Programs. If specified in the Program Documentation, readmes or notice files, such technology will be licensed to You under Separate Terms. Your rights to use Separately Licensed Technology under Separate Terms are not restricted in any way by the terms herein. For clarity, notwithstanding the existence of a notice, third party technology that is not Separately Licensed Technology shall be deemed part of the Programs licensed to You under the terms of this license.

Source Code for Open Source Software
For software that You receive from Oracle in binary form that is licensed under an open source license that gives You the right to receive the source code for that binary, You can obtain a copy of the applicable source code from https://oss.oracle.com/sources/ or http://www.oracle.com/goto/opensourcecode. If the source code for such software was not provided to You with the binary, You can also receive a copy of the source code on physical media by submitting a written request pursuant to the instructions in the "Written Offer for Source Code" section of the latter website.

-------------------------------------------------------------------------------
The following license terms apply to those Programs that are not provided to You under Separate Terms.
License Rights and Restrictions
Oracle grants to You, as a recipient of this Program, a nonexclusive, nontransferable, limited license to, subject to the conditions stated herein, use the unmodified Programs, including, without limitation, for the purposes of:
•	developing, testing, prototyping and demonstrating applications;
•	running the unmodified Programs for training, personal use, your business operations, and the business operations of third parties;
•	making the unmodified Programs available for use by third parties in your hosted environment and in cloud services;
•	redistributing unmodified Programs and Programs Documentation under the terms of this License; and
•	copying the unmodified Programs and Program Documentation to the extent reasonably necessary to exercise the license rights granted herein and for backup purposes.
For the purposes of this license, compiling, interpreting or configuring an otherwise unmodified Program as necessary to run the Program shall not be considered modification.

Your license is contingent on Your compliance with the following conditions:
- You include a copy of this license with any distribution by You of the Programs;
- You do not charge your customers, end users, distributees or other third parties any additional fees for the distribution or use of the Programs; however, for clarity, if you comply with the foregoing condition, distribution or use of the Program as part of your for-fee product or service that adds substantial additional value is permitted;
- You do not remove markings or notices of either Oracle's or a licensor's proprietary rights from the Programs or Program Documentation;
- You comply with all U.S. and applicable export control and economic sanctions laws and regulations that govern Your use of the Programs (including technical data); and
- You do not cause or permit reverse engineering, disassembly or decompilation of the Programs (except as allowed by law) by You nor allow an associated party to do so.
Any source code that may be included in the distribution with the Programs may not be modified, unless such source code is under Separate Terms permitting modification.
Ownership
Oracle or its licensors retain all ownership and intellectual property rights to the Programs.

Information Collection
The Programs' installation and/or auto-update processes, if any, may transmit a limited amount of data to Oracle or its service provider about those processes to help Oracle understand and optimize them. Oracle does not associate the data with personally identifiable information. Refer to Oracle's Privacy Policy at www.oracle.com/privacy.

Disclaimer of Warranties; Limitation of Liability
THE PROGRAMS ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND. ORACLE FURTHER DISCLAIMS ALL WARRANTIES, EXPRESS AND IMPLIED, INCLUDING WITHOUT LIMITATION, ANY IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, OR NONINFRINGEMENT.
IN NO EVENT UNLESS REQUIRED BY APPLICABLE LAW WILL ORACLE BE LIABLE TO YOU FOR DAMAGES, INCLUDING ANY GENERAL, SPECIAL, INCIDENTAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF THE USE OR INABILITY TO USE THE PROGRAM (INCLUDING BUT NOT LIMITED TO LOSS OF DATA OR DATA BEING RENDERED INACCURATE OR LOSSES SUSTAINED BY YOU OR THIRD PARTIES OR A FAILURE OF THE PROGRAM TO OPERATE WITH ANY OTHER PROGRAMS), EVEN IF SUCH HOLDER OR OTHER PARTY HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.

Version 1.0
Last updated:  28 June 2022
```

## ChilliCream.Nitro.App

`ChilliCream.Nitro.App` (ChilliCream, Inc.) is the GraphQL IDE assembly that arrives with
`HotChocolate.AspNetCore`. Its license file reads:

```text
Copyright (c) ChilliCream, Inc.

Source code in this repository is covered by the ChilliCream License 1.0 license as designated by a licensing file in a subdirectory or file header. The default throughout the repository is a license under the ChilliCream License 1.0, unless a file header or a licensing file in a subdirectory specifies another license.

---

# ChilliCream License 1.0

URL: https://chillicream.com/licensing/chillicream-license

## Acceptance

By using the software, you agree to all of the terms and conditions below.

## Copyright License

The licensor grants you a non-exclusive, royalty-free, worldwide,
non-sublicensable, non-transferable license to use, copy, distribute, make
available, and prepare derivative works of the software, in each case subject to
the limitations and conditions below.

## Limitations

You may not move, change, disable, or circumvent the license key functionality
in the software, and you may not remove or obscure any functionality in the
software that is protected by the license key.

You may not alter, remove, or obscure any licensing, copyright, or other notices
of the licensor in the software. Any use of the licensor’s trademarks is subject
to applicable law.

## Patents

The licensor grants you a license, under any patent claims the licensor can
license, or becomes able to license, to make, have made, use, sell, offer for
sale, import and have imported the software, in each case subject to the
limitations and conditions in this license. This license does not cover any
patent claims that you cause to be infringed by modifications or additions to
the software. If you or your company make any written claim that the software
infringes or contributes to infringement of any patent, your patent license for
the software granted under these terms ends immediately. If your company makes
such a claim, your patent license ends immediately for work on behalf of your
company.

## Notices

You must ensure that anyone who gets a copy of any part of the software from you
also gets a copy of these terms.

If you modify the software, you must include in any modified copies of the
software prominent notices stating that you have modified the software.

## No Other Rights

These terms do not imply any licenses other than those expressly granted in
these terms.

## Termination

If you use the software in violation of these terms, such use is not licensed,
and your licenses will automatically terminate. If the licensor provides you
with a notice of your violation, and you cease all violation of this license no
later than 30 days after you receive that notice, your licenses will be
reinstated retroactively. However, if you violate these terms after such
reinstatement, any additional violation of these terms will cause your licenses
to terminate automatically and permanently.

## No Liability

_As far as the law allows, the software comes as is, without any warranty or
condition, and the licensor will not be liable to you for any damages arising
out of these terms or the use or nature of the software, under any kind of
legal claim._

## Definitions

The **licensor** is the entity offering these terms, and the **software** is the
software the licensor makes available under these terms, including any portion
of it.

**you** refers to the individual or entity agreeing to these terms.

**your company** is any legal entity, sole proprietorship, or other kind of
organization that you work for, plus all organizations that have control over,
are under the control of, or are under common control with that
organization. **control** means ownership of substantially all the assets of an
entity, or the power to direct its management and policies by vote, contract, or
otherwise. Control can be direct or indirect.

**your licenses** are all the licenses granted to you for the software under
these terms.

**use** means anything you do with the software requiring one of your licenses.

**trademark** means trademarks, service marks, and similar rights.
```

## Components without a license declaration of their own

- `Oscar.Data.SqlClient` (copyright line `(c) 2003-2021` of Tianjin Shenzhou General Data
  Technology Co., Ltd.; the package metadata names microestc@outlook.com as author) declares no
  license: no license element, no license URL and no license file.
- `SqlSugarCore.Dm` and `SqlSugarCore.Kdbndp` declare Apache-2.0 for the package (see above).
  The vendor database drivers they bundle, `DM.DmProvider.dll` and `Kdbndp.dll`, carry no
  separate license declaration in the package.
