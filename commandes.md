docker build -t dperreaux/epsi4a-mspr4-msorder:0.5 .

docker push dperreaux/epsi4a-mspr4-msorder:0.5  

docker run -d -p 8080:8080 dperreaux/epsi4a-mspr4-msorder:tagname

## Rapport sous forme de tableau dans la console
docker run aquasec/trivy image dperreaux/epsi4a-mspr4-msorder:0.4

## Rapport sous forme de json dans la console
docker run aquasec/trivy  image --format json dperreaux/epsi4a-mspr4-msorder:0.4 --output /report/report.json

## Rapport sous forme de tableau dans un dossier
docker run -v C:\Code\epsi_4\mspr2\EPSI4A_MSPR4_MicroServiceCommande\trivy-reports:/report aquasec/trivy  image dperreaux/epsi4a-mspr4-msorder:0.4 --output /report/report_tableau_noble.txt

## Rapport sous forme de json dans un dossier
docker run -v C:\Code\epsi_4\mspr2\EPSI4A_MSPR4_MicroServiceCommande\trivy-reports:/report aquasec/trivy  image --format json dperreaux/epsi4a-mspr4-msorder:0.4 --output /report/report.json

## intégrer le .trivyignore
docker run -v C:\Code\epsi_4\mspr2\EPSI4A_MSPR4_MicroServiceCommande\trivy-reports:/report aquasec/trivy --ignorefile .trivyignore image --format json dperreaux/epsi4a-mspr4-msorder:0.4 --output /report/report.json

## Scanner vulnérabilités, secrets et misconf
docker run --rm -v C:\Code\epsi_4\mspr2\EPSI4A_MSPR4_MicroServiceCommande:/repo aquasec/trivy fs --scanners vuln,secret,misconfig ./

## sonarqube
D'abord : 
- docker pull sonarqube
- docker run -d --name sonarqube -p 9000:9000 -p 9092:9092 sonarqube

docker run -d --name sonarqube -p 9000:9000 -e SONAR_JDBC_URL=jdbc:postgresql://host.docker.internal:5432/sonarqube -e SONAR_JDBC_USERNAME=sonarqube -e SONAR_JDBC_PASSWORD=sonarqube -v sonarqube_data:/opt/sonarqube/data -v sonarqube_extensions:/opt/sonarqube/extensions -v sonarqube_logs:/opt/sonarqube/logs sonarqube

- création bdd postgre
- Création utilisateur sonarqube
- Donner les droits à l'utilisateur

dotnet sonarscanner begin /k:"ms_order" /d:sonar.host.url="http://localhost:9000"  /d:sonar.token="sqp_6420b353898e7f38921639017399c3545bdf1652"
dotnet build       
dotnet sonarscanner end /d:sonar.token="sqp_6420b353898e7f38921639017399c3545bdf1652"  

Actions : 
Implémenter IDisposable et pas son propre Dispose pour eviter ocnfusion
Enelver commentaires inutiles
Mettre namespace
Mettre les static en readonly et public
Pas de string interpolation dans les logs (performance)
PascaleCase dans les logs

## pour lire tous les tests
dotnet test --list-tests 

## pour ecrire dans la console pdt les tests
dotnet test --logger "console;verbosity=detailed"

dotnet sonarscanner begin /k:"ms_order" /d:sonar.host.url="http://localhost:9000" /d:sonar.token="sqp_6420b353898e7f38921639017399c3545bdf1652" /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/*.xml"
dotnet build
dotnet test --no-build --collect:"XPlat Code Coverage" --results-directory TestResults/
dotnet sonarscanner end /d:sonar.token="sqp_6420b353898e7f38921639017399c3545bdf1652"


docker tag dperreaux/epsi4a-mspr4-msorder:0.5 europe-west9-docker.pkg.dev/mspr4-464617/order/dperreaux/epsi4a-mspr4-msorder:0.5

