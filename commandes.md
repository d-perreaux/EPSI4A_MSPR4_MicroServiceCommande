docker build -t dperreaux/epsi4a-mspr4-msorder:0.4 .

docker push dperreaux/epsi4a-mspr4-msorder:0.4  

docker run -d -p 8080:8080 dperreaux/epsi4a-mspr4-msorder:tagname